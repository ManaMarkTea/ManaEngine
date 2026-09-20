// Web Audio owns the audio clock and mixing. No PCM callbacks into the .NET render loop.
const devices = new Map();
let nextDevice = 1;

export function createDevice() {
    const context = new AudioContext({ latencyHint: "interactive" });
    const id = nextDevice++;
    const abort = new AbortController();
    const device = { context, abort, buffers: new Map(), voices: new Map(), nextBuffer: 1, nextVoice: 1 };
    devices.set(id, device);
    // Keep these listeners until disposal: tab suspension may require another gesture to resume.
    const resume = () => {
        if (context.state !== "running" && context.state !== "closed") {
            context.resume().catch(error => console.warn("Audio resume failed", error));
        }
    };
    document.addEventListener("pointerdown", resume, { capture: true, signal: abort.signal });
    document.addEventListener("keydown", resume, { capture: true, signal: abort.signal });
    return id;
}

export function unlocked(id) { return devices.get(id)?.context.state === "running"; }

export function upload(id, samples, channels, rate) {
    const d = devices.get(id);
    const pcm = new DataView(samples.buffer, samples.byteOffset, samples.byteLength);
    const buffer = d.context.createBuffer(channels, samples.length / 2 / channels, rate);
    for (let channel = 0; channel < channels; channel++) {
        const output = buffer.getChannelData(channel);
        for (let frame = 0; frame < output.length; frame++) {
            output[frame] = pcm.getInt16((frame * channels + channel) * 2, true) / 32768;
        }
    }
    const handle = d.nextBuffer++;
    d.buffers.set(handle, buffer);
    return handle;
}

export function start(id, buffer, loop, spatial, volume, pitch, x, y, z, referenceDistance, maxDistance, rolloff) {
    const d = devices.get(id);
    if (!loop && d.context.state !== "running") { return 0; }
    const source = d.context.createBufferSource();
    const gain = d.context.createGain();
    let panner = null;
    source.buffer = d.buffers.get(buffer);
    source.loop = loop;
    source.playbackRate.value = pitch;
    gain.gain.value = volume;
    source.connect(gain);
    if (spatial) {
        panner = d.context.createPanner();
        panner.panningModel = "HRTF";
        panner.distanceModel = "inverse";
        panner.refDistance = referenceDistance;
        panner.maxDistance = maxDistance;
        panner.rolloffFactor = rolloff;
        panner.positionX.value = x;
        panner.positionY.value = y;
        panner.positionZ.value = z;
        gain.connect(panner);
        panner.connect(d.context.destination);
    } else {
        gain.connect(d.context.destination);
    }
    const handle = d.nextVoice++;
    const voice = { source, gain, panner };
    d.voices.set(handle, voice);
    source.onended = () => release(d, handle);
    // A loop scheduled in a suspended context starts when the context resumes.
    source.start();
    return handle;
}

function release(d, voice) {
    const v = d.voices.get(voice);
    if (!v) { return; }
    v.source.onended = null;
    v.source.disconnect();
    v.gain.disconnect();
    v.panner?.disconnect();
    d.voices.delete(voice);
}

export function playing(id, voice) { return devices.get(id)?.voices.has(voice) ?? false; }

export function change(id, voice, volume, pitch, x, y, z) {
    const d = devices.get(id);
    const v = d?.voices.get(voice);
    if (!v) { return; }
    v.gain.gain.setTargetAtTime(volume, d.context.currentTime, 0.01);
    v.source.playbackRate.value = pitch;
    if (v.panner) {
        v.panner.positionX.value = x;
        v.panner.positionY.value = y;
        v.panner.positionZ.value = z;
    }
}

export function stop(id, voice) {
    const d = devices.get(id);
    const v = d?.voices.get(voice);
    if (!v) { return; }
    v.source.stop();
    release(d, voice);
}

export function listener(id, x, y, z, fx, fy, fz, ux, uy, uz) {
    const l = devices.get(id).context.listener;
    l.positionX.value = x; l.positionY.value = y; l.positionZ.value = z;
    l.forwardX.value = fx; l.forwardY.value = fy; l.forwardZ.value = fz;
    l.upX.value = ux; l.upY.value = uy; l.upZ.value = uz;
}

export function destroy(id) {
    const d = devices.get(id);
    if (!d) { return; }
    d.abort.abort();
    for (const handle of [...d.voices.keys()]) { stop(id, handle); }
    d.buffers.clear();
    devices.delete(id);
    d.context.close().catch(error => console.warn("Audio close failed", error));
}
