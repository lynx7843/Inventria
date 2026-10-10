import { defineConfig } from '@playwright/test';

// Runs against an already-running stack (`docker compose up`, or the API and
// `npm run dev` started by hand): the test is only worth anything against the
// real backend, so nothing is mocked and nothing is started here.
export default defineConfig({
	testDir: 'e2e',
	use: {
		baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173'
	}
});
