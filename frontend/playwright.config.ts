import {defineConfig} from '@playwright/test';
import {cpSync, mkdirSync, mkdtempSync} from 'node:fs';
import {resolve} from 'node:path';
import {randomBytes} from 'node:crypto';

// Isolate each run from personal data; use only a copy of synthetic fixtures.
const external = process.env.PROJECTBRAIN_E2E_EXTERNAL === '1';
const apiUrl = process.env.PROJECTBRAIN_E2E_API_URL ?? 'http://127.0.0.1:14317';
const webUrl = process.env.PROJECTBRAIN_E2E_WEB_URL ?? 'http://127.0.0.1:14318';
process.env.PROJECTBRAIN_E2E_API_URL = apiUrl;
if (!external && !process.env.PROJECTBRAIN_E2E_DIR) {
  const local = resolve('..', '.local');
  mkdirSync(local, {recursive:true});
  process.env.PROJECTBRAIN_E2E_DIR = mkdtempSync(resolve(local, 'browser-'));
  process.env.PROJECTBRAIN_E2E_SOURCES = resolve(process.env.PROJECTBRAIN_E2E_DIR, 'sources');
  cpSync(resolve('..', 'tests', 'fixtures'), process.env.PROJECTBRAIN_E2E_SOURCES, {recursive:true});
  process.env.PROJECTBRAIN_AUTH_TOKEN = randomBytes(32).toString('hex');
}
export default defineConfig({
  testDir:'./e2e', workers:1, timeout:90000,
  use:{baseURL:webUrl,trace:'retain-on-failure',screenshot:'only-on-failure'},
  webServer: external ? undefined : [
    {command:'dotnet run --project ../src/ProjectBrain.Api --no-launch-profile --no-build',
      url:apiUrl+'/health/ready',reuseExistingServer:false,timeout:60000,
      env:{ASPNETCORE_URLS:apiUrl,ASPNETCORE_ENVIRONMENT:'Production',PROJECTBRAIN_WEB_ORIGIN:webUrl,
        PROJECTBRAIN_AUTH_TOKEN:process.env.PROJECTBRAIN_AUTH_TOKEN!,PROJECTBRAIN_AGENT_TOKEN:'',
        PROJECTBRAIN_SOURCES:process.env.PROJECTBRAIN_E2E_SOURCES!,
        PROJECTBRAIN_DB:resolve(process.env.PROJECTBRAIN_E2E_DIR!, 'data', 'brain.db')}},
    {command:'npm run dev -- --port '+new URL(webUrl).port,url:webUrl,reuseExistingServer:false,
      env:{PROJECTBRAIN_DEV_API_URL:apiUrl}}
  ],reporter:'list'
});

