// On-screen controls, in the scheme the level's game modes ask for (ManaTouchControls):
//   stick   a stick for W/A/S/D, drag to look, Run, Jump and Camera -- a character walking about
//   swipe   nothing on screen; a swipe taps that arrow key, a tap is a click -- a runner
//   pointer nothing on screen; a finger is the mouse, a pinch the wheel -- menus and boards
//   twin    a stick to move, a stick that aims and fires, and the mode's own buttons -- a twin-stick shooter
// Pointer capture keeps independent fingers on movement, look and action buttons.
const SWIPE_DISTANCE = 26;
const PINCH_PIXELS_PER_NOTCH = 40;

// A press shorter than a frame is one that game code reading held keys and buttons never sees.
const MINIMUM_HOLD_MS = 60;

export function installTouchControls(graphics, openMenu, game) {
    const enabled = new URLSearchParams(location.search).get('rendering') === 'mobile' ||
        (navigator.maxTouchPoints > 0 && matchMedia('(pointer: coarse)').matches);
    if (!enabled) return () => {};
    const root = document.getElementById('manaTouch');
    const canvas = document.getElementById('manaCanvas');
    const move = document.getElementById('manaMove');
    const knob = document.getElementById('manaMoveKnob');
    const look = document.getElementById('manaLook');
    const aim = document.getElementById('manaAim');
    const aimKnob = document.getElementById('manaAimKnob');
    const rotate = document.getElementById('manaRotate');
    // Safari's long-press menu and text selection compete with held game inputs.
    for (const surface of [root, canvas]) {
        for (const name of ['contextmenu', 'selectstart', 'dragstart']) {
            surface.addEventListener(name, event => event.preventDefault());
        }
    }
    const keys = new Set();
    const pointers = new Map();
    let ready = false;
    let scheme = 'stick';
    const key = (code, down) => {
        if (keys.has(code) === down) return;
        if (down) { keys.add(code); graphics.OnKeyDown(code, false); }
        else { keys.delete(code); graphics.OnKeyUp(code); }
    };
    const tapKey = code => {
        key(code, true);
        setTimeout(() => key(code, false), MINIMUM_HOLD_MS);
    };
    const release = id => {
        const pointer = pointers.get(id);
        if (!pointer) { return; }
        pointers.delete(id);
        if (pointer.kind === 'move') {
            for (const code of ['KeyW','KeyA','KeyS','KeyD']) { key(code, false); }
            knob.style.transform = '';
        } else if (pointer.kind === 'look') { graphics.OnMouseUp(2); }
        else if (pointer.kind === 'aim') { game?.SetTouchAim(0, 0); aimKnob.style.transform = ''; }
        else {
            const held = performance.now() - pointer.since;
            setTimeout(() => key(pointer.code, false), Math.max(0, MINIMUM_HOLD_MS - held));
        }
        if (pointer.element.hasPointerCapture(id)) { pointer.element.releasePointerCapture(id); }
    };

    // ----- the canvas: a finger as the mouse, a pinch as the wheel, or a swipe as an arrow key -----------------
    const fingers = new Map();
    let mouseDownAt = -1;
    const canvasPoint = event => {
        const rect = canvas.getBoundingClientRect();
        return [event.clientX - rect.left, event.clientY - rect.top];
    };
    const mouseUp = () => {
        if (mouseDownAt < 0) { return; }
        const held = performance.now() - mouseDownAt;
        mouseDownAt = -1;
        setTimeout(() => graphics.OnMouseUp(0), Math.max(0, MINIMUM_HOLD_MS - held));
    };
    const pinchWidth = () => {
        const [a, b] = [...fingers.values()];
        return Math.hypot(a.x - b.x, a.y - b.y);
    };
    const canvasReset = () => {
        fingers.clear();
        mouseUp();
    };
    // Otherwise the browser follows a touch with mouse events of its own, and each tap lands twice.
    canvas.addEventListener('touchstart', event => {
        if (ready && scheme !== 'stick') { event.preventDefault(); }
    }, {passive:false});
    canvas.addEventListener('pointerdown', event => {
        if (!ready || globalThis.ManaMenuOpen || event.pointerType === 'mouse' || scheme === 'stick' || scheme === 'twin') return;
        event.preventDefault();
        canvas.setPointerCapture(event.pointerId);
        const finger = {x:event.clientX, y:event.clientY, startX:event.clientX, startY:event.clientY, swiped:false};
        fingers.set(event.pointerId, finger);
        if (scheme === 'swipe') { return; }
        if (fingers.size === 1) {
            graphics.OnMouseMove(...canvasPoint(event));
            graphics.OnMouseDown(0);
            mouseDownAt = performance.now();
        } else if (fingers.size === 2) {
            // Two fingers are a zoom, never a click at wherever the first came down.
            mouseUp();
            finger.pinch = pinchWidth();
        }
    });
    canvas.addEventListener('pointermove', event => {
        const finger = fingers.get(event.pointerId);
        if (!finger) { return; }
        event.preventDefault();
        finger.x = event.clientX;
        finger.y = event.clientY;
        if (scheme === 'swipe') {
            const dx = finger.x - finger.startX;
            const dy = finger.y - finger.startY;
            if (!finger.swiped && Math.hypot(dx, dy) > SWIPE_DISTANCE) {
                finger.swiped = true;
                if (Math.abs(dx) > Math.abs(dy)) { tapKey(dx < 0 ? 'ArrowLeft' : 'ArrowRight'); }
                else { tapKey(dy < 0 ? 'ArrowUp' : 'ArrowDown'); }
            }
        } else if (fingers.size === 1) {
            graphics.OnMouseMove(...canvasPoint(event));
        } else if (fingers.size === 2) {
            const holder = [...fingers.values()].find(f => f.pinch !== undefined);
            if (holder) {
                const width = pinchWidth();
                graphics.OnMouseWheel(0, (width - holder.pinch) / PINCH_PIXELS_PER_NOTCH);
                holder.pinch = width;
            }
        }
    });
    const canvasEnd = event => {
        const finger = fingers.get(event.pointerId);
        if (!finger) { return; }
        fingers.delete(event.pointerId);
        if (scheme === 'swipe') {
            // A touch that never became a swipe is a tap: the Play Again and the menus still want clicking.
            if (!finger.swiped && event.type === 'pointerup') {
                graphics.OnMouseMove(...canvasPoint(event));
                graphics.OnMouseDown(0);
                mouseDownAt = performance.now();
                mouseUp();
            }
        } else if (fingers.size === 0) {
            mouseUp();
        }
    };
    canvas.addEventListener('pointerup', canvasEnd);
    canvas.addEventListener('pointercancel', canvasEnd);

    const reset = () => {
        for (const id of [...pointers.keys()]) { release(id); }
        for (const code of [...keys]) { key(code, false); }
        canvasReset();
    };
    // Safari can lose a captured pointer's end event. Touch identifiers provide a second release path.
    document.addEventListener('touchstart', event => {
        for (const touch of event.changedTouches) {
            const match = [...pointers.values()].find(p => p.touchId === undefined &&
                Math.hypot(p.x - touch.clientX, p.y - touch.clientY) < 20);
            if (match) { match.touchId = touch.identifier; }
        }
    }, {passive:true, capture:true});
    document.addEventListener('touchend', event => {
        const active = new Set([...event.touches].map(t => t.identifier));
        for (const [id, pointer] of [...pointers]) {
            if (active.size === 0 || (pointer.touchId !== undefined && !active.has(pointer.touchId))) {
                release(id);
            }
        }
        if (active.size === 0) { canvasReset(); }
    }, {passive:true, capture:true});
    document.addEventListener('touchcancel', reset, {passive:true, capture:true});
    for (const name of ['pointerup', 'pointercancel']) {
        document.addEventListener(name, event => release(event.pointerId), true);
    }
    const stickAt = (event, element) => {
        const rect = element.getBoundingClientRect();
        const radius = rect.width * 0.35;
        let x = (event.clientX - rect.left - rect.width / 2) / radius;
        let y = (event.clientY - rect.top - rect.height / 2) / radius;
        const length = Math.hypot(x, y);
        if (length > 1) { x /= length; y /= length; }
        return {x, y, radius};
    };
    const updateMove = event => {
        const {x, y, radius} = stickAt(event, move);
        knob.style.transform = `translate(${x * radius}px, ${y * radius}px)`;
        key('KeyA', x < -0.25); key('KeyD', x > 0.25);
        key('KeyW', y < -0.25); key('KeyS', y > 0.25);
    };
    const updateAim = event => {
        const {x, y, radius} = stickAt(event, aim);
        aimKnob.style.transform = `translate(${x * radius}px, ${y * radius}px)`;
        game?.SetTouchAim(x, y);
    };
    const bind = (element, kind, code) => {
        element.addEventListener('pointerdown', event => {
            if (!ready || globalThis.ManaMenuOpen || event.pointerType === 'mouse') return;
            for (const [id, pointer] of [...pointers]) {
                if (pointer.element === element) { release(id); }
            }
            event.preventDefault();
            element.setPointerCapture(event.pointerId);
            pointers.set(event.pointerId, {element, kind, code, x:event.clientX, y:event.clientY, since:performance.now()});
            if (kind === 'move') updateMove(event);
            else if (kind === 'aim') updateAim(event);
            else if (kind === 'look') {
                const rect = canvas.getBoundingClientRect();
                graphics.OnMouseMove(event.clientX - rect.left, event.clientY - rect.top);
                graphics.OnMouseDown(2);
            } else key(code, true);
        });
        element.addEventListener('pointermove', event => {
            const pointer = pointers.get(event.pointerId);
            if (!pointer) return;
            if (event.buttons === 0) { release(event.pointerId); return; }
            event.preventDefault();
            if (kind === 'move') updateMove(event);
            else if (kind === 'aim') updateAim(event);
            else if (kind === 'look') {
                graphics.OnMouseDelta(event.clientX - pointer.x, event.clientY - pointer.y);
                pointer.x = event.clientX; pointer.y = event.clientY;
            }
        });
        const end = event => release(event.pointerId);
        for (const name of ['pointerup','pointercancel','lostpointercapture']) element.addEventListener(name, end);
    };
    bind(move, 'move'); bind(look, 'look'); bind(aim, 'aim');
    bind(document.getElementById('manaTouchJump'), 'key', 'Space');
    bind(document.getElementById('manaTouchRun'), 'key', 'ShiftLeft');
    bind(document.getElementById('manaTouchCamera'), 'key', 'KeyC');
    for (const button of document.querySelectorAll('#manaTwinButtons button')) {
        bind(button, 'key', button.dataset.key);
    }
    document.getElementById('manaTouchMenu').addEventListener('click', () => {
        reset();
        // A mode with a pause of its own has its own way out; Escape is how it is asked.
        if (game?.ModeHandlesEscape()) { tapKey('Escape'); }
        else { openMenu(); }
    });
    document.addEventListener('mana-pause', () => { reset(); root.hidden = Boolean(globalThis.ManaMenuOpen); });
    document.addEventListener('visibilitychange', () => { if (document.hidden) reset(); });
    window.addEventListener('blur', reset);
    window.addEventListener('resize', reset);
    window.addEventListener('pagehide', reset);
    window.addEventListener('pageshow', reset);

    // A game can change scheme as it goes: Contour's menus want a finger, its flying wants sticks.
    const follow = () => {
        const wanted = game?.TouchControls() ?? 'stick';
        if (wanted !== scheme) {
            reset();
            scheme = wanted;
        }
        root.dataset.scheme = scheme;
        rotate.hidden = !(game?.PrefersLandscape() && innerHeight > innerWidth);
    };
    return () => {
        ready = true;
        root.hidden = false;
        document.getElementById('manaHint').hidden = true;
        document.body.classList.add('mana-touch-play');
        follow();
        setInterval(follow, 150);
    };
}
