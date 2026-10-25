import {test,expect} from '@playwright/test';
test('real local API: capture hostile text, Try Next, and reload persistence',async({page})=>{
  const token=process.env.PROJECTBRAIN_AUTH_TOKEN;
  test.skip(!token,'Start the API on 4317 and supply PROJECTBRAIN_AUTH_TOKEN.');
  await page.goto('/');
  await page.getByLabel('Local access token').fill(token!);
  await page.getByRole('button',{name:'Open workspace'}).click();
  await page.getByRole('button',{name:/Quick Capture/}).click();
  const note='Fixture idea '+Date.now()+' <script>window.pwned=true</script>';
  await page.getByLabel('Your note').fill(note);
  await page.getByLabel('I want to try this').check();
  await page.getByRole('button',{name:'Save to Inbox',exact:true}).click();
  await expect(page.getByRole('status').filter({hasText:'Saved to Inbox'})).toBeVisible();
  await page.getByRole('navigation').getByRole('button',{name:'Try Next',exact:true}).click();
  await expect(page.getByRole('button',{name:note})).toBeVisible();
  expect(await page.evaluate(()=>Object.prototype.hasOwnProperty.call(window,'pwned'))).toBe(false);
  await page.reload();
  await page.getByLabel('Local access token').fill(token!);await page.getByRole('button',{name:'Open workspace'}).click();
  await page.getByRole('navigation').getByRole('button',{name:'Idea Inbox'}).click();
  await expect(page.getByRole('button',{name:note})).toBeVisible();
});

