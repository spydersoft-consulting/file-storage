import { defineConfig } from '@playwright/test';
import { execSync } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, existsSync } from 'node:fs';
import path from 'node:path';

const authDir = path.join(__dirname, '.auth');
const tokenFile = path.join(authDir, 'token.json');
const readOnlyTokenFile = path.join(authDir, 'token-readonly.json');
const tokenGenProject = path.resolve(
  __dirname, '../../src/Spydersoft.FileStore.TokenGenerator');
const appHostProject = path.resolve(
  __dirname, '../../src/Spydersoft.FileStore.AppHost');
const baseUrl = process.env.FILESTORE_BASE_URL ?? 'http://localhost:5300';

function runTokenGen(file: string, extraArgs: string): string {
  if (!existsSync(file)) {
    try {
      const output = execSync(`dotnet run --project "${tokenGenProject}" -- ${extraArgs}`, {
        encoding: 'utf-8',
        stdio: ['ignore', 'pipe', 'ignore'],
        timeout: 60_000,
      });
      const json = output.split('\n').map((l: string) => l.trim()).find((l: string) => l.startsWith('{')) ?? '{}';
      const token = (JSON.parse(json) as { token?: string }).token ?? '';
      mkdirSync(authDir, { recursive: true });
      writeFileSync(file, JSON.stringify({ token }));
      return token;
    } catch {
      return '';
    }
  }
  try {
    return JSON.parse(readFileSync(file, 'utf-8')).token ?? '';
  } catch {
    return '';
  }
}

const token = process.env.FILESTORE_TEST_TOKEN ?? runTokenGen(tokenFile, '');
export const readOnlyToken = runTokenGen(readOnlyTokenFile, '--read-only');

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: 'html',
  use: {
    baseURL: baseUrl,
    ignoreHTTPSErrors: true,
    extraHTTPHeaders: {
      Authorization: `Bearer ${token}`,
    },
  },
  webServer: {
    command: `dotnet run --project "${appHostProject}" --launch-profile Testing`,
    url: `${baseUrl}/livez`,
    timeout: 300_000,
    reuseExistingServer: !process.env.CI,
  },
});
