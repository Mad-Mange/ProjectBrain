import { useEffect, useRef, useState } from 'react';
import {api,kinds,label,type Project} from './api';
type Props={projects:Project[];onClose:()=>void;onSaved:(id:number)=>void};
export default function QuickCapture({projects,onClose,onSaved}:Props) {
  const dialog=useRef<HTMLDialogElement>(null); const text=useRef<HTMLTextAreaElement>(null);
  const [raw,setRaw]=useState(''); const [title,setTitle]=useState(''); const [kind,setKind]=useState('idea');
  const [url,setUrl]=useState(''); const [project,setProject]=useState(''); const [tags,setTags]=useState(''); const [tryNext,setTryNext]=useState(false);
  const [error,setError]=useState(''); const [busy,setBusy]=useState(false); const retry=useRef<{json:string;key:string}|null>(null);
  useEffect(()=>{dialog.current?.showModal(); text.current?.focus(); const d=dialog.current; return ()=>d?.close();},[]);
  async function save(another:boolean) {
    setBusy(true);setError('');
    const input={originalText:raw,title:title||null,kind,sourceUrl:url||null,projectId:project||null,tags:tags.split(',').map(t=>t.trim()).filter(Boolean),intentFlag:tryNext?'try_next':'none'};
    const json=JSON.stringify(input);
    if(retry.current?.json!==json) retry.current={json,key:crypto.randomUUID()};
    try { const result=await api<{knowledgeId:number}>('/captures','POST',input,retry.current!.key); if(another){setRaw('');setTitle('');setUrl('');setTags('');retry.current=null;text.current?.focus();} onSaved(result.knowledgeId); if(!another)onClose(); }
    catch(e){setError((e as Error).message+' Your text is still here. Retry when the local service is available.');}
    finally{setBusy(false);}
  }
  return <dialog ref={dialog} onCancel={onClose} aria-labelledby="capture-title" className="capture-dialog">
    <div className="dialog-heading"><div><span className="eyebrow">SAVE THE THOUGHT</span><h2 id="capture-title">Quick Capture</h2></div><button onClick={onClose} aria-label="Close Quick Capture" className="icon-button">×</button></div>
    <p className="muted">An idea is enough. Add the details when you're ready.</p>
    <label>Type<select value={kind} onChange={e=>setKind(e.target.value)}>{kinds.filter(k=>k!=='project_profile').map(k=><option key={k} value={k}>{label(k)}</option>)}</select></label>
    <label>Your note <span className="muted">required</span><textarea ref={text} rows={7} value={raw} onChange={e=>setRaw(e.target.value)} placeholder="Paste an idea, a lesson, or a useful snippet…" maxLength={65536}/></label>
    <label>Title <span className="muted">optional</span><input value={title} onChange={e=>setTitle(e.target.value)} maxLength={200}/></label>
    <div className="form-grid"><label>Source URL<input type="url" value={url} onChange={e=>setUrl(e.target.value)} placeholder="https://…" maxLength={2048}/></label><label>Project<select value={project} onChange={e=>setProject(e.target.value)}><option value="">Unassigned</option>{projects.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label></div>
    <label>Tags <span className="muted">comma separated</span><input value={tags} onChange={e=>setTags(e.target.value)} placeholder="unreal, cpp, architecture"/></label>
    <label className="check"><input type="checkbox" checked={tryNext} onChange={e=>setTryNext(e.target.checked)}/> I want to try this</label>
    <p className="muted small">Saved locally as Draft · Untested. URLs are references and are never fetched.</p>
    {error&&<p role="alert" className="error">{error}</p>}
    <footer><button className="secondary" onClick={onClose} disabled={busy}>Cancel</button><button className="secondary" disabled={!raw.trim()||busy} onClick={()=>void save(true)}>Save & add another</button><button disabled={!raw.trim()||busy} onClick={()=>void save(false)}>{busy?'Saving…':'Save to Inbox'}</button></footer>
  </dialog>;
}

