let credential = '';
export function setToken(token:string) { credential = token; }
export async function api<T=unknown>(path:string,method='GET',body?:unknown,key?:string):Promise<T> {
  const response = await fetch('/api/v1'+path,{method,headers:{Authorization:'Bearer '+credential,...(body!==undefined?{'Content-Type':'application/json'}:{}),...(method!=='GET'?{'Idempotency-Key':key??crypto.randomUUID()}: {})},body:body===undefined?undefined:JSON.stringify(body)});
  if(!response.ok) {
    let message = 'Request failed ('+response.status+').';
    try { const problem=await response.json(); message=problem.detail??problem.title??message; } catch { /* non-JSON proxy failure */ }
    if(response.status===401) message='Session needs a token. Sign out and enter it again.';
    throw new Error(message);
  }
  return response.json() as Promise<T>;
}
export async function download(path:string,filename:string) {
  const response=await fetch('/api/v1'+path,{headers:{Authorization:'Bearer '+credential}});
  if(!response.ok) throw new Error('Download failed.');
  const url=URL.createObjectURL(await response.blob()); const a=document.createElement('a'); a.href=url; a.download=filename; a.click(); URL.revokeObjectURL(url);
}
export type Evidence={projectId:string;relativePath:string;fileSha256?:string;note?:string;stale?:number};
export type Knowledge={id:number;title:string;summary:string;body?:string;kind:string;reviewStatus:string;validationStatus:string;intentFlag:string;confidence:string;needsReview:boolean;isVerified:boolean;contentVersion:number;projectId:string|null;tags:string[];evidence:Evidence[];sourceUnavailable?:boolean;capture?:{id:string;originalText:string;sourceUrl:string|null};relations?:{knowledgeId:number;relationType:string}[]};
export type Project={id:string;name:string;kind:string;relativePath:string;status:string;revision:number};
export type Root={id:string;alias:string;availability:string;lastSuccessUtc:string|null;nextScanUtc:string|null;enabled:number;contentVersion:number};
export type Job={id:string;status:string;mode:string;countsJson:string;errorSummary:string|null};
export type Run={id:string;knowledgeId:number;hypothesis:string;successCriteria:string;procedure:string;limitations:string;environmentJson:string;status:string;outcome:string;contentVersion:number;reviewedBy:string|null;evidenceJson:string;proposals:{id:string;proposedOutcome:string;observations:string;evidenceJson:string;reviewStatus:string;reviewReason:string|null}[]};
export const kinds=['idea','best_practice','lesson_learned','reusable_solution','project_profile'];
export const label=(value:string)=>value.replaceAll('_',' ').replace(/^./,c=>c.toUpperCase());

