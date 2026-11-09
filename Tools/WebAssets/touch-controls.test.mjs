import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
class Element {
    handlers = new Map(); captures = new Set(); style = {}; hidden = true;
    addEventListener(name, callback) { this.handlers.set(name, callback); }
    setPointerCapture(id) { this.captures.add(id); }
    hasPointerCapture(id) { return this.captures.has(id); }
    releasePointerCapture(id) { this.captures.delete(id); this.fire('lostpointercapture', {pointerId:id}); }
    getBoundingClientRect() { return {left:0,top:0,width:128,height:128}; }
    fire(name, fields={}) { this.handlers.get(name)?.({pointerType:'touch',preventDefault(){},...fields}); }
}
const names=['manaTouch','manaMove','manaMoveKnob','manaLook','manaTouchJump','manaTouchRun','manaTouchCamera','manaTouchMenu','manaCanvas','manaHint'];
const elements = Object.fromEntries(names.map(name=>[name,new Element()]));
const docEvents=new Map(), windowEvents=new Map(), held=new Set(), deltas=[];
let mouse=false;
globalThis.location={search:'?rendering=mobile'};
Object.defineProperty(globalThis,'navigator',{configurable:true,value:{maxTouchPoints:5}});
globalThis.matchMedia=()=>({matches:true});
globalThis.window={addEventListener:(name,callback)=>windowEvents.set(name,callback)};
globalThis.document={hidden:false,getElementById:name=>elements[name],
    addEventListener:(name,callback)=>docEvents.set(name,callback),body:{classList:{add(){}}}};
const graphics={OnKeyDown:code=>held.add(code),OnKeyUp:code=>held.delete(code),
    OnMouseMove(){},OnMouseDown:()=>mouse=true,OnMouseUp:()=>mouse=false,OnMouseDelta:(x,y)=>deltas.push([x,y])};
const source=await readFile(new URL('../../ManaEditor.Web/touch-controls.js',import.meta.url),'utf8');
const {installTouchControls}=await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const ready=installTouchControls(graphics,()=>{globalThis.ManaMenuOpen=true;docEvents.get('mana-pause')();});
const move=elements.manaMove, jump=elements.manaTouchJump, look=elements.manaLook;
move.fire('pointerdown',{pointerId:1,clientX:64,clientY:10});
assert.equal(held.size,0,'loading cannot start movement');
ready();
assert.equal(elements.manaTouch.hidden,false);
for (const surface of [elements.manaTouch, elements.manaCanvas]) {
    for (const name of ['contextmenu', 'selectstart', 'dragstart']) {
        let prevented = false;
        surface.fire(name, {preventDefault() { prevented = true; }});
        assert.equal(prevented, true, `${name} cannot interrupt touch play`);
    }
}
move.fire('pointerdown',{pointerId:1,clientX:100,clientY:10});
assert.deepEqual([...held].sort(),['KeyD','KeyW'],'stick supports diagonals');
jump.fire('pointerdown',{pointerId:2});
assert.ok(held.has('Space') && held.has('KeyW'),'jump and movement work simultaneously');
look.fire('pointerdown',{pointerId:3,clientX:50,clientY:50});
look.fire('pointermove',{pointerId:3,clientX:63,clientY:44});
assert.equal(mouse,true); assert.deepEqual(deltas,[[13,-6]],'look uses mouse deltas without pointer lock');
jump.fire('pointercancel',{pointerId:2});
assert.equal(held.has('Space'),false,'cancel releases actions');
move.fire('pointermove',{pointerId:1,clientX:64,clientY:64});
assert.equal(held.size,0,'stick centre is a dead zone');
move.fire('pointermove',{pointerId:1,clientX:64,clientY:100});
assert.ok(held.has('KeyS'));
elements.manaTouchMenu.fire('click');
assert.equal(held.size,0); assert.equal(mouse,false); assert.equal(elements.manaTouch.hidden,true);
globalThis.ManaMenuOpen=false; docEvents.get('mana-pause')();
assert.equal(elements.manaTouch.hidden,false,'resume restores controls');
move.fire('pointerdown',{pointerId:4,clientX:10,clientY:64});
windowEvents.get('resize')();
assert.equal(held.size,0,'rotation resets held movement');
elements.manaTouchRun.fire('pointerdown',{pointerId:5});
assert.ok(held.has('ShiftLeft'));
document.hidden=true; docEvents.get('visibilitychange')();
assert.equal(held.size,0,'backgrounding releases held actions');
document.hidden=false;
move.fire('pointerdown',{pointerId:10,clientX:64,clientY:10});
jump.fire('pointerdown',{pointerId:11,clientX:100,clientY:100});
docEvents.get('touchstart')({changedTouches:[{identifier:20,clientX:64,clientY:10},{identifier:21,clientX:100,clientY:100}]});
docEvents.get('touchend')({touches:[{identifier:21}]});
assert.deepEqual([...held],['Space'],'missing pointerup releases only the lifted finger');
docEvents.get('touchend')({touches:[]});
assert.equal(held.size,0,'last touch end recovers all held controls');
move.fire('pointerdown',{pointerId:12,clientX:10,clientY:64});
move.fire('pointerdown',{pointerId:13,clientX:100,clientY:64});
assert.deepEqual([...held],['KeyD'],'a fresh touch replaces an abandoned pointer');
docEvents.get('pointerup')({pointerId:13});
assert.equal(held.size,0,'release outside the control is handled by the document');
look.fire('pointerdown',{pointerId:14,clientX:50,clientY:50});
docEvents.get('touchcancel')();
assert.equal(mouse,false,'Safari gesture cancellation releases look');
globalThis.location={search:''}; navigator.maxTouchPoints=0;
const previous=elements.manaTouch.hidden;
installTouchControls(graphics,()=>{})();
assert.equal(elements.manaTouch.hidden,previous,'desktop leaves touch controls disabled');
console.log('Touch controls checks passed.');
