import {label,type Knowledge} from './api';
export function Badges({item}:{item:Knowledge}) {
  return <div className="badges"><span className="badge">{label(item.kind)}</span><span className="badge">{label(item.reviewStatus)}</span><span className={'badge '+(item.isVerified?'success':'')}>{item.isVerified?'Tested & Works':label(item.validationStatus)}</span><span className="badge intent">{item.intentFlag==='try_next'?'Want to try':label(item.intentFlag)}</span>{item.needsReview&&<span className="badge warning">Needs recheck</span>}{item.sourceUnavailable&&<span className="badge warning">Source unavailable</span>}</div>;
}
export default function KnowledgeCard({item,onOpen}:{item:Knowledge;onOpen:(id:number)=>void}) {
  return <article className="knowledge-card"><Badges item={item}/><button className="title-button" onClick={()=>onOpen(item.id)}>{item.title}</button><p>{item.summary}</p><div className="card-bottom"><span>{item.tags.map(t=>'#'+t).join(' ')||'No tags yet'}</span><span>{item.evidence.length} source{item.evidence.length!==1?'s':''}</span></div></article>;
}

