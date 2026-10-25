import {render,screen,fireEvent,waitFor,cleanup} from '@testing-library/react';
import {afterEach,beforeEach,expect,it,vi} from 'vitest';
import QuickCapture from './QuickCapture';
import KnowledgeCard from './KnowledgeCard';
import {setToken,type Knowledge} from './api';
beforeEach(()=>setToken('synthetic-test-token'));
afterEach(()=>{cleanup();vi.unstubAllGlobals();});
it('captures raw text with no project, tags, AI or title',async()=>{
  const fetch=vi.fn().mockResolvedValue({ok:true,json:async()=>({knowledgeId:42})});vi.stubGlobal('fetch',fetch);
  const saved=vi.fn();render(<QuickCapture projects={[]} onClose={vi.fn()} onSaved={saved}/>);
  fireEvent.change(screen.getByLabelText(/Your note/),{target:{value:'  original\n  indentation  '}});
  fireEvent.click(screen.getByText('Save to Inbox'));
  await waitFor(()=>expect(saved).toHaveBeenCalledWith(42));
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toMatchObject({originalText:'  original\n  indentation  ',kind:'idea',projectId:null,intentFlag:'none'});
});
it('retains text and idempotency key when a save fails',async()=>{
  const fetch=vi.fn().mockRejectedValueOnce(new Error('Offline')).mockResolvedValueOnce({ok:true,json:async()=>({knowledgeId:7})});vi.stubGlobal('fetch',fetch);
  render(<QuickCapture projects={[]} onClose={vi.fn()} onSaved={vi.fn()}/>);
  fireEvent.change(screen.getByLabelText(/Your note/),{target:{value:'Do not lose this'}});
  fireEvent.click(screen.getByText('Save to Inbox'));await screen.findByRole('alert');
  expect(screen.getByLabelText(/Your note/)).toHaveValue('Do not lose this');
  fireEvent.click(screen.getByText('Save to Inbox'));
  await waitFor(()=>expect(fetch).toHaveBeenCalledTimes(2));
  expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe(fetch.mock.calls[1][1].headers['Idempotency-Key']);
});
it('renders hostile content inert and does not infer verification from type',()=>{
  const item:Knowledge={id:1,title:'<script>window.pwned=true</script>',summary:'<img src=x onerror=alert(1)>',kind:'best_practice',reviewStatus:'draft',validationStatus:'untested',intentFlag:'try_next',confidence:'low',needsReview:false,isVerified:false,contentVersion:1,projectId:null,tags:[],evidence:[]};
  const {container}=render(<KnowledgeCard item={item} onOpen={vi.fn()}/>);
  expect(container.querySelector('script')).toBeNull();expect(container.querySelector('img')).toBeNull();
  expect(screen.getByText('Want to try')).toBeInTheDocument();expect(screen.queryByText('Tested & Works')).toBeNull();
  expect(screen.getByText('Untested')).toBeInTheDocument();
});

