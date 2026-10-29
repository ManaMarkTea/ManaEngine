// GPU regression: runs the production particle-motion function and reads its positions back.
// Requires Node 22+ and Chrome, like webdrive.mjs. No wasm workload required.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const out = resolve(process.argv[2] ?? 'Artifacts/GpuParticleWorldBrowser');
mkdirSync(out, { recursive: true });
const source = readFileSync(new URL('../../ManaGraphics/Assets/Shaders/WebGpu/particles.wgsl', import.meta.url), 'utf8');
const test = async function(source) {
  try {
    const adapter = await navigator.gpu.requestAdapter();
    const device = await adapter.requestDevice();
    device.pushErrorScope('validation');
    const module = device.createShaderModule({code: source + `
@group(0) @binding(2) var<storage, read_write> positions: array<vec4<f32>>;
@compute @workgroup_size(1) fn regression(@builtin(global_invocation_id) id: vec3<u32>) {
    let launch = particle_launch(id.x);
    positions[id.x] = vec4<f32>(position_at(launch, launch.age), launch.t);
}`});
    const info = await module.getCompilationInfo();
    if(info.messages.some(m=>m.type==='error')) throw Error(info.messages.map(m=>m.message).join('\n'));
    const compute = await device.createComputePipelineAsync({layout:'auto',compute:{module,entryPoint:'regression'}});
    const renderLayout = device.createBindGroupLayout({entries:[
      {binding:0,visibility:GPUShaderStage.VERTEX|GPUShaderStage.FRAGMENT,buffer:{type:'uniform',minBindingSize:416}},
      {binding:1,visibility:GPUShaderStage.VERTEX,buffer:{type:'read-only-storage',minBindingSize:16384}}
    ]});
    const render = await device.createRenderPipelineAsync({layout:device.createPipelineLayout({bindGroupLayouts:[renderLayout]}),
      vertex:{module,entryPoint:'vs_main'},fragment:{module,entryPoint:'fs_main',targets:[{format:'rgba16float',blend:{color:{srcFactor:'src-alpha',dstFactor:'one',operation:'add'},alpha:{srcFactor:'zero',dstFactor:'one',operation:'add'}}}]},
      primitive:{topology:'triangle-list',cullMode:'none'},depthStencil:{format:'depth24plus',depthWriteEnabled:false,depthCompare:'less-equal'}});
    const uniform = device.createBuffer({size:416,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
    const history = device.createBuffer({size:16384,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST});
    const output = device.createBuffer({size:160,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_SRC});
    const staging = device.createBuffer({size:160,usage:GPUBufferUsage.MAP_READ|GPUBufferUsage.COPY_DST});
    const group = device.createBindGroup({layout:compute.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:uniform}},{binding:1,resource:{buffer:history}},{binding:2,resource:{buffer:output}}]});
    const samples = new Float32Array(4096);
    const identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    const moved = [0,1,0,0,-1,0,0,0,0,0,1,0,10,2,3,1];
    for(let i=0;i<256;i++) samples.set(identity,i*16);
    const data = new ArrayBuffer(416), f = new Float32Array(data), u = new Uint32Array(data);
    f[0]=f[5]=0.1; f[10]=0.01; f[14]=0.5; f[15]=1;
    f.set(moved,16); f[32]=1; f[37]=1;
    f.set([0,1,2,1],40); f.set([0,-4,0,0],44); f.set([0.1,0.02,0,1],48);
    f.set([1,0.5,0.1,1],52); f.set([1,0.1,0,0],56); u.set([10,17,0,0],60);
    async function positionsAt(time) {
      const tick = Math.floor(time*254);
      f[40]=time%1; u[63]=tick%256; f[64]=tick%254; f[65]=1/254;
      device.queue.writeBuffer(uniform,0,data); device.queue.writeBuffer(history,0,samples);
      const encoder=device.createCommandEncoder(); const pass=encoder.beginComputePass();
      pass.setPipeline(compute); pass.setBindGroup(0,group); pass.dispatchWorkgroups(10); pass.end();
      encoder.copyBufferToBuffer(output,0,staging,0,160); device.queue.submit([encoder.finish()]);
      await staging.mapAsync(GPUMapMode.READ); const values=new Float32Array(staging.getMappedRange()).slice(); staging.unmap(); return values;
    }
    function near(values,index,expected,label) {
      const actual=Array.from(values.slice(index*4,index*4+3));
      if(actual.some((v,i)=>!Number.isFinite(v)||Math.abs(v-expected[i])>0.003)) throw Error(label+': '+actual+' expected '+expected);
    }
    near(await positionsAt(0.5),0,[0,0.5,0],'prewarmed particle');
    for(let tick=128;tick<=165;tick++) samples.set(moved,(tick%256)*16);
    near(await positionsAt(0.65),0,[0,0.455,0],'old particle ignores moved emitter');
    near(await positionsAt(0.65),4,[9.9,1.995,3],'rotated launch with world gravity');
    f[28]=100; f[29]=100; // Another gizmo move must not change either emitted particle.
    for(let tick=166;tick<=177;tick++) samples.set([0,1,0,0,-1,0,0,0,0,0,1,0,100,100,3,1],tick*16);
    near(await positionsAt(0.7),4,[9.8,1.98,3],'birth pose stays fixed as ring advances');
    for(let tick=178;tick<=279;tick++) samples.set(moved,(tick%256)*16);
    near(await positionsAt(1.1),0,[9.8,1.98,3],'respawn across lifetime and ring wrap');
    // World-space floor and parallel walls, using the production motion path.
    const down = [1,0,0,0,0,-1,0,0,0,0,-1,0,0,0.5,0,1];
    for(let i=0;i<256;i++) samples.set(down,i*16);
    f.set([0,0,0,0],44); f.set([0,1,0,0],68); f.set([1,0,0,0],72); f.set([1,0,1,0],76);
    near(await positionsAt(0.5),0,[0,0.5,0],'elastic floor bounce');
    f[78]=0.5;
    near(await positionsAt(0.5),0,[0,0.25,0],'restitution loses normal speed');
    f[78]=1; f[45]=-4;
    // y=.5-2t-2t² crosses at (sqrt(2)-1)/2; normal speed is sqrt(8).
    const impact=(Math.sqrt(2)-1)/2, after=0.5-impact;
    near(await positionsAt(0.5),0,[0,Math.sqrt(8)*after-2*after*after,0],'accelerated floor impact');
    f[78]=0;
    f[45]=-4;
    near(await positionsAt(0.8),0,[0,0,0],'zero restitution rests under gravity');
    f[45]=0; f[78]=1;
    const sideways=[0,-1,0,0,1,0,0,0,0,0,1,0,0.5,0,0,1];
    for(let i=0;i<256;i++) samples.set(sideways,i*16);
    f.set([1,0,0,0],68); f.set([-1,0,0,1],72); f[77]=1;
    near(await positionsAt(0.8),0,[0.1,0,0],'repeated bounces between two planes');
    f[47]=1; f[77]=0;
    const towardFloor=[1,0,0,0,0,-1,0,0,0,0,-1,0,0,0.5,0,1];
    for(let i=0;i<256;i++) samples.set(towardFloor,i*16);
    f.set([0,1,0,0],68);
    near(await positionsAt(0.5),0,[0,0.2869387,0],'bounce with linear drag');
    f[47]=0; f[77]=0;
    const inv=Math.SQRT1_2;
    f.set([inv,inv,0,0],68);
    near(await positionsAt(0.5),0,[0.5,0,0],'inclined plane preserves tangential motion');
    // A downward dip must collide even when its eventual uncollided endpoint is above the floor.
    f.set([0,1,0,0],68); f[45]=8;
    for(let i=0;i<256;i++) samples.set([1,0,0,0,0,-1,0,0,0,0,-1,0,0,0.1,0,1],i*16);
    const dipHit=(2-Math.sqrt(2.4))/8, dipAfter=0.8-dipHit;
    near(await positionsAt(0.8),0,[0,Math.sqrt(2.4)*dipAfter+4*dipAfter*dipAfter,0],'collision before upward turnaround');
    // The global wind is world-space air velocity, independent of emitter rotation.
    f[76]=f[77]=0; f.set([0,0,0,0],44); f.set([3,0,-2,1],80);
    for(let i=0;i<256;i++) samples.set(moved,i*16);
    near(await positionsAt(0.5),0,[10.5,2,2],'global wind advects a rotated emitter');
    f[47]=1;
    near(await positionsAt(0.5),0,[10-2*(1-Math.exp(-0.5))+1.5,2,2],'drag is relative to moving air');
    f[83]=0;
    near(await positionsAt(0.5),0,[10-2*(1-Math.exp(-0.5)),2,3],'wind influence can be disabled');
    f[83]=1; f[47]=0; f.set([0,1,0,0],68); f[76]=1;
    for(let i=0;i<256;i++) samples.set(down,i*16);
    near(await positionsAt(0.5),0,[1.5,0.5,-1],'wind preserves tangential motion through a floor bounce');
    // Swirl turns the free flight about the birth axis: a quarter turn at this age, round +Y through the origin.
    for(let i=0;i<256;i++) samples.set(identity,i*16);
    f[76]=f[77]=0; const still=await positionsAt(0.5);
    f[84]=Math.PI; const turned=await positionsAt(0.5);
    near(turned,0,[still[2],still[1],-still[0]],'swirl turns a particle about its birth axis');
    f[84]=0;
    // Validate the actual instanced HDR/depth render path as well as compute readback.
    const renderGroup=device.createBindGroup({layout:renderLayout,entries:[{binding:0,resource:{buffer:uniform}},{binding:1,resource:{buffer:history}}]});
    const color=device.createTexture({size:[64,64],format:'rgba16float',usage:GPUTextureUsage.RENDER_ATTACHMENT});
    const depth=device.createTexture({size:[64,64],format:'depth24plus',usage:GPUTextureUsage.RENDER_ATTACHMENT});
    const encoder=device.createCommandEncoder(); const pass=encoder.beginRenderPass({colorAttachments:[{view:color.createView(),loadOp:'clear',storeOp:'store',clearValue:[0,0,0,1]}],depthStencilAttachment:{view:depth.createView(),depthLoadOp:'clear',depthStoreOp:'store',depthClearValue:1}});
    pass.setPipeline(render); pass.setBindGroup(0,renderGroup); pass.draw(6,10); pass.end(); device.queue.submit([encoder.finish()]);
    await device.queue.onSubmittedWorkDone(); const error=await device.popErrorScope(); if(error) throw Error(error.message);
    document.body.textContent='PASS: GPU particle readback preserves birth positions, world gravity, rotated launches and ring/lifetime wrap; global wind, floor, restitution, resting contact, two-plane bounces, drag and swirl validated; render pipeline validated.';
    console.log(document.body.textContent);
  } catch(error) { document.body.textContent='FAIL: '+error.stack; console.error(document.body.textContent); }
};
writeFileSync(resolve(out,'test.js'),`(${test.toString()})(${JSON.stringify(source)});`);
writeFileSync(resolve(out,'index.html'),'<title>GPU particle world-space regression</title><body><script type="module">' + readFileSync(resolve(out,'test.js'),'utf8') + '</script>');
const steps=[{go:pathToFileURL(resolve(out,'index.html')).href},{waitFor:'PASS:',timeout:30000},{shot:'result'}];
const result=spawnSync(process.execPath,[fileURLToPath(new URL('./webdrive.mjs',import.meta.url)),resolve(out,'Chrome'),JSON.stringify(steps)],{encoding:'utf8'});
process.stdout.write(result.stdout ?? ''); process.stderr.write(result.stderr ?? '');
if(result.status!==0 || !result.stdout?.includes('PASS:') || result.stdout?.includes('FAIL:')) process.exit(1);
