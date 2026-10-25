// Syntax-only fallback when npm restore is unavailable. This is not a React typecheck or browser test.
const fs=require('node:fs');
const path=require('node:path');
const ts=require(process.argv[2]||'typescript');
const files=[];
function walk(dir){for(const entry of fs.readdirSync(dir,{withFileTypes:true})){const p=path.join(dir,entry.name);if(entry.isDirectory()){if(!['node_modules','dist'].includes(entry.name))walk(p);}else if(/\.tsx?$/.test(p))files.push(p);}}
walk('frontend/src');walk('frontend/e2e');files.push('frontend/vite.config.ts','frontend/playwright.config.ts');
let errors=0;
for(const file of files){
 const result=ts.transpileModule(fs.readFileSync(file,'utf8'),{fileName:file,compilerOptions:{jsx:ts.JsxEmit.ReactJSX,target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ESNext,isolatedModules:true},reportDiagnostics:true});
 for(const d of result.diagnostics||[]){if(d.category===ts.DiagnosticCategory.Error){errors++;process.stderr.write(file+': '+ts.flattenDiagnosticMessageText(d.messageText,'\n')+'\n');}}
}
console.log('TypeScript '+ts.version+': '+files.length+' files, '+errors+' syntax errors. Dependency-aware typechecking NOT performed.');
process.exitCode=errors?1:0;

