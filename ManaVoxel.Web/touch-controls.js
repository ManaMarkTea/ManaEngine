// Pointer capture keeps independent fingers on movement, look and action buttons.
export function installTouchControls(graphics, openMenu) {
    const enabled = new URLSearchParams(location.search).get('rendering') === 'mobile' ||
        (navigator.maxTouchPoints > 0 && matchMedia('(pointer: coarse)').matches);
    if (!enabled) return () => {};
    const root = document.getElementById('manaTouch');
    const move = document.getElementById('manaMove');
    const knob = document.getElementById('manaMoveKnob');
    const look = document.getElementById('manaLook');
    // Safari's long-press menu and text selection compete with held game inputs.
    for (const surface of [root, document.getElementById('manaCanvas')]) {
        for (const name of ['contextmenu', 'selectstart', 'dragstart']) {
            surface.addEventListener(name, event => event.preventDefault());
        }
    }
    const keys = new Set();
    const pointers = new Map();
    let ready = false;
    const key = (code, down) => {
        if (keys.has(code) === down) return;
        if (down) { keys.add(code); graphics.OnKeyDown(code, false); }
        else { keys.delete(code); graphics.OnKeyUp(code); }
    };
    const release = id => {
        const pointer = pointers.get(id);
        if (!pointer) { return; }
        pointers.delete(id);
        if (pointer.kind === 'move') {
            for (const code of ['KeyW','KeyA','KeyS','KeyD']) { key(code, false); }
            knob.style.transform = '';
        } else if (pointer.kind === 'look') { graphics.OnMouseUp(2); }
        else { key(pointer.code, false); }
        if (pointer.element.hasPointerCapture(id)) { pointer.element.releasePointerCapture(id); }
    };
    const reset = () => {
        for (const id of [...pointers.keys()]) { release(id); }
        for (const code of [...keys]) { key(code, false); }
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
    }, {passive:true, capture:true});
    document.addEventListener('touchcancel', reset, {passive:true, capture:true});
    for (const name of ['pointerup', 'pointercancel']) {
        document.addEventListener(name, event => release(event.pointerId), true);
    }
    const updateMove = event => {
        const rect = move.getBoundingClientRect();
        const radius = rect.width * 0.35;
        let x = (event.clientX - rect.left - rect.width / 2) / radius;
        let y = (event.clientY - rect.top - rect.height / 2) / radius;
        const length = Math.hypot(x, y);
        if (length > 1) { x /= length; y /= length; }
        knob.style.transform = `translate(${x * radius}px, ${y * radius}px)`;
        key('KeyA', x < -0.25); key('KeyD', x > 0.25);
        key('KeyW', y < -0.25); key('KeyS', y > 0.25);
    };
    const bind = (element, kind, code) => {
        element.addEventListener('pointerdown', event => {
            if (!ready || globalThis.ManaMenuOpen || event.pointerType === 'mouse') return;
            for (const [id, pointer] of [...pointers]) {
                if (pointer.element === element) { release(id); }
            }
            event.preventDefault();
            element.setPointerCapture(event.pointerId);
            pointers.set(event.pointerId, {element, kind, code, x:event.clientX, y:event.clientY});
            if (kind === 'move') updateMove(event);
            else if (kind === 'look') {
                const rect = document.getElementById('manaCanvas').getBoundingClientRect();
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
            else if (kind === 'look') {
                graphics.OnMouseDelta(event.clientX - pointer.x, event.clientY - pointer.y);
                pointer.x = event.clientX; pointer.y = event.clientY;
            }
        });
        const end = event => release(event.pointerId);
        for (const name of ['pointerup','pointercancel','lostpointercapture']) element.addEventListener(name, end);
    };
    bind(move, 'move'); bind(look, 'look');
    bind(document.getElementById('manaTouchJump'), 'key', 'Space');
    bind(document.getElementById('manaTouchRun'), 'key', 'ShiftLeft');
    bind(document.getElementById('manaTouchCamera'), 'key', 'KeyC');
    document.getElementById('manaTouchMenu').addEventListener('click', () => { reset(); openMenu(); });
    document.addEventListener('mana-pause', () => { reset(); root.hidden = Boolean(globalThis.ManaMenuOpen); });
    document.addEventListener('visibilitychange', () => { if (document.hidden) reset(); });
    window.addEventListener('blur', reset);
    window.addEventListener('resize', reset);
    window.addEventListener('pagehide', reset);
    window.addEventListener('pageshow', reset);
    return () => {
        ready = true;
        root.hidden = false;
        document.getElementById('manaHint').hidden = true;
        document.body.classList.add('mana-touch-play');
    };
}
