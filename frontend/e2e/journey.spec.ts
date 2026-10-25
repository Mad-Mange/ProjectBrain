import {test, expect} from '@playwright/test';
import {writeFileSync} from 'node:fs';
import {join} from 'node:path';
import {randomUUID} from 'node:crypto';

test('inventory, capture, reviewed experiment, changed proof, and backup', async ({page, request}, testInfo) => {
  const token = process.env.PROJECTBRAIN_AUTH_TOKEN!;
  const apiUrl = process.env.PROJECTBRAIN_E2E_API_URL!;
  const sources = process.env.PROJECTBRAIN_E2E_SOURCES!;
  const title = 'Golden journey idea '+Date.now();
  const hypothesis = 'Proof for '+title;
  expect(token).toBeTruthy(); expect(sources).toBeTruthy();
  async function api(path:string, data?:unknown) {
    const response = await request.fetch(apiUrl+'/api/v1'+path, {
      method:data===undefined?'GET':'POST', data,
      headers:{Authorization:'Bearer '+token,'Idempotency-Key':randomUUID()}
    });
    expect(response.ok(), await response.text()).toBeTruthy(); return response.json();
  }
  const root = await api('/roots', {relativePath:'',alias:'Acceptance fixtures'});
  async function scan() {
    const job = await api('/scans',{rootId:root.id,mode:'incremental'});
    await expect.poll(async () => (await api('/scans/'+job.id)).status, {timeout:40000}).toBe('succeeded');
  }
  await scan();
  const projects = await api('/projects');
  expect(projects.map((p:{kind:string})=>p.kind)).toEqual(expect.arrayContaining(['dotnet','python','unreal','unity','blender','javascript','docker']));
  const active = projects.find((p:{name:string})=>p.name==='ActiveExperiment');
  await page.goto('/');
  await page.getByLabel('Local access token').fill(token);
  await page.getByRole('button',{name:'Open workspace'}).click();
  await page.getByRole('navigation').getByRole('button',{name:'Projects',exact:true}).click();
  await expect(page.getByRole('button',{name:'BasicDotNet',exact:true})).toBeVisible();
  await page.keyboard.press('Control+k');
  await page.getByLabel('Your note').fill('Separate game rules from rendering for repeatable experiments.');
  await page.getByLabel('Title optional').fill(title);
  await page.getByLabel('I want to try this').check();
  await page.getByRole('button',{name:'Save to Inbox',exact:true}).click();
  await page.getByRole('navigation').getByRole('button',{name:'Try Next',exact:true}).click();
  await page.getByRole('button',{name:title,exact:true}).click();
  await expect(page.locator('.badges').getByText('Untested',{exact:true})).toBeVisible();
  await page.getByLabel('Review reason').fill('A useful hypothesis for this synthetic test.');
  await page.getByRole('button',{name:'Approve',exact:true}).click();
  await expect(page.locator('.badges').getByText('Approved',{exact:true})).toBeVisible();
  await expect(page.locator('.badges').getByText('Tested & Works',{exact:true})).toHaveCount(0);
  await page.getByRole('button',{name:'Create experiment',exact:true}).click();
  await page.getByLabel('Hypothesis',{exact:true}).fill(hypothesis);
  await page.getByLabel('Measurable success criteria').fill('All fixture assertions pass');
  await page.getByLabel('Target project').selectOption(active.id);
  await page.getByRole('button',{name:'Accept experiment plan'}).click();
  await expect(page.getByRole('heading',{name:'Plan an experiment'})).toHaveCount(0);
  const found = await api('/knowledge/search?q=Golden%20journey%20idea&kind=idea');
  const item = found.results.find((k:{title:string})=>k.title===title);
  const [run] = await api('/knowledge/'+item.id+'/experiments');
  const rpc = await request.post(apiUrl+'/mcp', {
    headers:{Authorization:'Bearer '+token,Accept:'application/json, text/event-stream','MCP-Protocol-Version':'2025-11-25'},
    data:{jsonrpc:'2.0',id:1,method:'tools/call',params:{name:'propose_experiment_result',arguments:{
      knowledgeId:item.id,experimentId:run.id,proposedOutcome:'passed',observations:'Synthetic fixture assertions passed',
      evidence:[{projectId:active.id,relativePath:'tests/results.txt'}],clientProposalId:randomUUID()}}}
  });
  expect(rpc.ok(), await rpc.text()).toBeTruthy(); expect(await rpc.text()).toContain('pending');
  expect((await api('/knowledge/'+item.id)).isVerified).toBe(false);
  await page.getByRole('navigation').getByRole('button',{name:/^Review Queue/}).click();
  const review = page.getByRole('article').filter({has:page.getByRole('heading',{name:hypothesis,exact:true})});
  await review.getByLabel('Review reason').fill('Checked the synthetic artifact and success criteria.');
  await review.getByLabel('I inspected the current evidence and success criteria').check();
  await review.getByRole('button',{name:'Accept result',exact:true}).click();
  await expect.poll(async()=>(await api('/knowledge/'+item.id)).isVerified).toBe(true);
  await page.getByRole('navigation').getByRole('button',{name:'Try Next',exact:true}).click();
  await page.getByRole('button',{name:title,exact:true}).click();
  await expect(page.locator('.badges').getByText('Tested & Works',{exact:true})).toBeVisible();
  await page.screenshot({path:testInfo.outputPath('reviewed-proof.png'),fullPage:true});
  writeFileSync(join(sources,'ActiveExperiment','tests','results.txt'),'FAIL: synthetic changed evidence\n');
  await scan();
  expect((await api('/knowledge/'+item.id)).validationStatus).toBe('stale');
  expect((await api('/knowledge/'+item.id+'/experiments'))[0].outcome).toBe('passed');
  await page.getByRole('navigation').getByRole('button',{name:'Try Next',exact:true}).click();
  await page.getByRole('button',{name:title,exact:true}).click();
  await expect(page.locator('.badges').getByText('Needs recheck',{exact:true})).toBeVisible();
  await page.screenshot({path:testInfo.outputPath('stale-proof.png'),fullPage:true});
  const backup=await api('/backups',{});
  const download=await request.get(apiUrl+backup.downloadUrl,{headers:{Authorization:'Bearer '+token}});
  expect(download.ok()).toBeTruthy(); expect((await download.body()).subarray(0,15).toString()).toBe('SQLite format 3');
});
