"""Writes a story from an idea with Claude, checks it against what the engine has, and (optionally) films it.

    export ANTHROPIC_API_KEY=...                      # or `ant auth login` once
    python Tools/Stories/generate.py "Sam Altman took my RAM; getting it back took a heist"
    python Tools/Stories/generate.py "<idea>" --seconds 20 --draft      # write it, then a quick half-size render
    python Tools/Stories/generate.py "<idea>" --render                  # write it, then the full 1080 x 1920 MP4

Claude gets the story format (Tools/Stories/README.md) and the catalog of everything a story can use
(Tools/Stories/catalog.py: levels, models with their sizes, characters and their animations, music, sounds), and
answers with a story as JSON. story.py's checker reads it; whatever it finds wrong goes back to Claude to fix, up to
three times. The story lands in Content/Stories/<title>.json -- edit it by hand and re-run story.py as much as you like.
"""
import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import catalog  # noqa: E402
import story as story_tool  # noqa: E402

ROOT = story_tool.ROOT
MODEL = "claude-opus-5-5"
ATTEMPTS = 3

FORMAT = (Path(__file__).resolve().parent / "README.md")

DIRECTION = """\
You write short vertical videos (Instagram / TikTok stories, 1080 x 1920 portrait) that ManaEngine films: a 3D engine
with a retro look -- big pixels, dithered colours, a worn VHS or CRT -- like the lo-fi pixel 3D clips people post. You
answer with one story as JSON in the format below, using only the levels, models, characters, clips, prefabs, music,
sounds and fonts listed in the catalog. A path that is not in the catalog does not exist.

How to make it good:
- Tell a tiny story with a beginning, a turn and a punchline, in 3-7 shots of 2-6 seconds. The first second must hook.
- Portrait framing: the screen is tall and narrow. Keep the subject in the middle; frame people from close (2-4 m away,
  camera at chest or eye height, fov 55-70). Wide establishing shots need the camera higher and further back.
- Build sets: the Empty Stage level is a bare floor centred on [48, 0, 48] (24 m square) -- furnish it from the model
  packs (a control room from the space station kit, an office from the furniture kit...). Scale the furniture kit up
  (about 2.5x) so it fits people; characters from kenney_blocky-characters_20 want scale 0.65. Put the set where the
  camera looks: camera target and the props within a few metres of each other.
- Things placed in a shot's "set" stay for the following shots on the same level; use "clear": true to start a set over.
- Act with the timeline: walk people in, turn them to face each other or the camera ("face"), play clips (emote-yes,
  emote-no, pick-up, sit, interact-right, die...). A walk's "seconds" sets its pace: about 1.4 m a second looks natural.
- Titles are big words drawn in the scene (they get the retro filter, like an old game's title) -- one to three words,
  at the top ("position": [0, 0.5]) or middle. Captions are the subtitles under the picture: short, punchy, first
  person; at most about 12 words each.
- Music: pick one track for the whole story. Sounds: a few well-timed effects (a notification, a whoosh, an impact).
- Pick the retro look that fits: VHS (worn tape), Console (clean pixels), CRT (curved glass), Default (in between).
- Coordinates must make sense: people stand on the ground (y = 0 on the Empty Stage), the camera looks at them.

Answer with the JSON only, in a ```json fenced block, nothing else.
"""


def system_blocks():
    """The direction, the format and the catalog: the same every time, so they are cached between attempts and runs."""
    text = DIRECTION + "\n\n# The story format\n\n" + FORMAT.read_text(encoding="utf-8") + "\n\n" + catalog.build()
    return [{"type": "text", "text": text, "cache_control": {"type": "ephemeral", "ttl": "1h"}}]


def extract_json(text):
    match = re.search(r"```(?:json)?\s*(\{.*\})\s*```", text, flags=re.DOTALL)
    candidate = match.group(1) if match else text[text.find("{"):text.rfind("}") + 1]
    return json.loads(candidate)


def ask(client, messages):
    import anthropic

    try:
        with client.beta.messages.stream(
            model=MODEL,
            max_tokens=32000,
            thinking={"type": "adaptive"},
            output_config={"effort": "high"},
            # Opus 5.5's safety classifiers can decline; the server then retries on the recommended fallback model.
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
            system=system_blocks(),
            messages=messages,
        ) as stream:
            response = stream.get_final_message()
    except anthropic.AuthenticationError:
        sys.exit("No working Anthropic API key: export ANTHROPIC_API_KEY=... (or run `ant auth login`).")
    except anthropic.RateLimitError:
        sys.exit("Rate limited by the Anthropic API; wait a minute and run it again.")
    except anthropic.APIStatusError as error:
        sys.exit(f"The Anthropic API refused the request ({error.status_code}): {error.message}")
    except anthropic.APIConnectionError:
        sys.exit("Could not reach the Anthropic API (network).")

    if response.stop_reason == "refusal":
        detail = ""
        if response.stop_details:
            detail = f" ({response.stop_details.category}: {response.stop_details.explanation})"
        sys.exit(f"Claude declined to write this story{detail}. Try rewording the idea.")
    if response.stop_reason == "max_tokens":
        print("The answer ran out of room; asking again.")
    usage = response.usage
    print(f"  ({usage.input_tokens} new input tokens, {usage.cache_read_input_tokens or 0} cached, "
          f"{usage.output_tokens} output)")
    text = "".join(block.text for block in response.content if block.type == "text")
    return response, text


def generate(idea, seconds, retro=None, client=None):
    """A checked story for the idea, or exits explaining why there is none."""
    if client is None:
        import anthropic

        client = anthropic.Anthropic()
    request = f"Idea: {idea}\n\nAim for about {seconds} seconds in all."
    if retro:
        request += f" Use the {retro} retro look."
    messages = [{"role": "user", "content": request}]
    problems = ["(no answer yet)"]
    story = None
    for attempt in range(1, ATTEMPTS + 1):
        print(f"Asking {MODEL} (attempt {attempt}) ...")
        response, text = ask(client, messages)
        messages.append({"role": "assistant", "content": response.content})
        try:
            story = story_tool.normalize(extract_json(text))
            problems = story_tool.validate(story)
        except (json.JSONDecodeError, ValueError) as error:
            story, problems = None, [f"The answer was not valid JSON ({error}). Answer with the whole story as JSON."]
        if not problems:
            return story
        print("  The story has problems; sending them back:\n    " + "\n    ".join(problems))
        messages.append({"role": "user", "content": "The checker found these problems. Fix every one and answer with "
                         "the whole corrected story as JSON:\n- " + "\n- ".join(problems)})
    sys.exit("Claude's story still had problems after " f"{ATTEMPTS} tries:\n  " + "\n  ".join(problems))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("idea", help="what the video is about, in a sentence or a paragraph")
    parser.add_argument("--seconds", type=int, default=15, help="roughly how long (default 15)")
    parser.add_argument("--retro", choices=story_tool.RETRO_LOOKS, help="insist on a retro look")
    parser.add_argument("--out", help="where to save the story (default Content/Stories/<title>.json)")
    parser.add_argument("--draft", action="store_true", help="then render a quick half-size preview")
    parser.add_argument("--render", action="store_true", help="then render the full-size MP4")
    options = parser.parse_args()

    story = generate(options.idea, options.seconds, options.retro)
    out = Path(options.out) if options.out else ROOT / "Content" / "Stories" / f"{story['title'].title()}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(story, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    total = sum(float(shot["seconds"]) for shot in story["shots"])
    print(f"Wrote {out.relative_to(ROOT) if out.is_relative_to(ROOT) else out}: \"{story['title']}\", "
          f"{len(story['shots'])} shots, {total:g}s")
    if options.draft or options.render:
        story_tool.make(story, draft=options.draft and not options.render)


if __name__ == "__main__":
    main()
