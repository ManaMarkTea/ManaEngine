// Render the production shader and read pixels back: gradients, night stars, and Photo mode.
import {readFileSync, writeFileSync, mkdirSync} from 'node:fs';
import {spawnSync} from 'node:child_process';
import {resolve} from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
const out = resolve(process.argv[2] ?? 'artifacts/MobileSkyShader');
mkdirSync(out, {recursive:true});
// The engine resolves #include before compiling (ManaWgslPreprocessor); so does this, one level deep.
const shaderDir = new URL('../../ManaGraphics/Assets/Shaders/WebGpu/', import.meta.url);
const source = readFileSync(new URL('skybox_mobile.wgsl', shaderDir),'utf8')
  .replace(/^[ \t]*#include[ \t]+"([^"]+)"[ \t]*$/gm, (_, path) => readFileSync(new URL(path, shaderDir),'utf8'));
const check = async source => {
  try {
    const adapter = await navigator.gpu.requestAdapter();
    const device = await adapter.requestDevice();
    device.pushErrorScope('validation');
    const module = device.createShaderModule({code:source});
    const info = await module.getCompilationInfo();
    if (info.messages.some(m=>m.type==='error')) throw Error(info.messages.map(m=>m.message).join('\n'));
    const layout = device.createBindGroupLayout({entries:[
      {binding:0,visibility:GPUShaderStage.VERTEX|GPUShaderStage.FRAGMENT,buffer:{type:'uniform'}},
      {binding:1,visibility:GPUShaderStage.FRAGMENT,sampler:{type:'filtering'}},
      {binding:2,visibility:GPUShaderStage.FRAGMENT,texture:{viewDimension:'cube'}},
      {binding:3,visibility:GPUShaderStage.FRAGMENT,texture:{viewDimension:'2d'}},
      {binding:4,visibility:GPUShaderStage.FRAGMENT,buffer:{type:'read-only-storage'}}
    ]});
    const pipeline = await device.createRenderPipelineAsync({layout:device.createPipelineLayout({bindGroupLayouts:[layout]}),
      vertex:{module,entryPoint:'vs_main'},fragment:{module,entryPoint:'fs_main',targets:[{format:'rgba8unorm'}]}});
    const uniform = device.createBuffer({size:160,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
    const cube = device.createTexture({size:[1,1,6],format:'rgba8unorm',usage:GPUTextureUsage.TEXTURE_BINDING|GPUTextureUsage.COPY_DST});
    for(let z=0;z<6;z++) device.queue.writeTexture({texture:cube,origin:[0,0,z]},new Uint8Array([32,64,128,255]),{bytesPerRow:4},[1,1,1]);
    const moon = device.createTexture({size:[1,1],format:'rgba8unorm',usage:GPUTextureUsage.TEXTURE_BINDING});
    const group = device.createBindGroup({layout,entries:[{binding:0,resource:{buffer:uniform}},
      {binding:1,resource:device.createSampler({minFilter:'linear',magFilter:'linear'})},
      {binding:2,resource:cube.createView({dimension:'cube'})},{binding:3,resource:moon.createView()},
      {binding:4,resource:{buffer:device.createBuffer({size:(128+32+101+20)*16,usage:GPUBufferUsage.STORAGE})}}]});
    const width=256,height=128;
    const target=device.createTexture({size:[width,height],format:'rgba8unorm',usage:GPUTextureUsage.RENDER_ATTACHMENT|GPUTextureUsage.COPY_SRC});
    const readback=device.createBuffer({size:width*height*4,usage:GPUBufferUsage.COPY_DST|GPUBufferUsage.MAP_READ});
    const values=new Float32Array(40); values[0]=values[5]=values[10]=values[15]=1;
    values[17]=1; // Sun overhead, outside this test camera's view.
    async function draw(mode,daylight,title) {
      values[19]=mode; values[25]=daylight;
      device.queue.writeBuffer(uniform,0,values);
      const encoder=device.createCommandEncoder();
      const pass=encoder.beginRenderPass({colorAttachments:[{view:target.createView(),loadOp:'clear',storeOp:'store',clearValue:[0,0,0,1]}]});
      pass.setPipeline(pipeline); pass.setBindGroup(0,group); pass.draw(3); pass.end();
      encoder.copyTextureToBuffer({texture:target},{buffer:readback,bytesPerRow:width*4},[width,height]);
      device.queue.submit([encoder.finish()]);
      await readback.mapAsync(GPUMapMode.READ);
      const pixels=new Uint8Array(readback.getMappedRange()).slice(); readback.unmap();
      const canvas=document.createElement('canvas'); canvas.width=width; canvas.height=height;
      canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(pixels),width,height),0,0);
      const label=document.createElement('p'); label.textContent=title; document.body.append(label,canvas);
      return pixels;
    }
    const day=await draw(1,1,'Day');
    const night=await draw(1,0,'Night and stars');
    const space=await draw(2,1,'Space');
    const photo=await draw(0,1,'Photo texture');
    values[39]=1; // celestial_north.w: the star catalogue on (?sky=stars).
    const catalogue=await draw(1,0,'Night with the catalogue'); values[39]=0;
    let bright=0,dayBlue=0,nightBlue=0;
    for(let i=0;i<night.length;i+=4){ if(night[i]>70) bright++; dayBlue+=day[i+2]; nightBlue+=night[i+2]; }
    if(dayBlue<=nightBlue*5) throw Error('day/night gradients do not respond to daylight');
    if(bright<5) throw Error('night sky did not draw stars');
    if(space[2]>=day[2]) throw Error('space did not select its dark palette');
    let catalogueBright=0; for(let i=0;i<catalogue.length;i+=4){ if(catalogue[i]>70) catalogueBright++; }
    if(catalogueBright<5) throw Error('night sky with the catalogue on lost its stars');
    if(photo[0]!==32 || photo[1]!==64 || photo[2]!==128) throw Error('Photo mode did not sample the environment cube');
    const error=await device.popErrorScope(); if(error) throw Error(error.message);
    console.log(`PASS: mobile sky pipeline, day/night gradients, ${bright} star pixels, the catalogue switch, space and Photo validated.`);
  } catch(error) {console.error('FAIL: '+error.stack);}
};
writeFileSync(resolve(out,'index.html'),`<body style="background:#171a20;color:#aaa"><script type="module">(${check.toString()})(${JSON.stringify(source)});</script>`);
const steps=[{go:pathToFileURL(resolve(out,'index.html')).href},{waitFor:'PASS:',timeout:30000},{shot:'skies'}];
const result=spawnSync(process.execPath,[fileURLToPath(new URL('./webdrive.mjs',import.meta.url)),resolve(out,'Chrome'),JSON.stringify(steps)],{encoding:'utf8'});
process.stdout.write(result.stdout ?? ''); process.stderr.write(result.stderr ?? '');
if(result.status!==0 || !result.stdout?.includes('PASS:') || result.stdout?.includes('FAIL:')) process.exit(1);
