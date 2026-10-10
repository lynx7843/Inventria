import { expect, test } from '@playwright/test';

const username = process.env.E2E_USERNAME ?? 'admin';
const password = process.env.E2E_PASSWORD ?? 'password';

test('log in, create an item, receive stock, see the count change', async ({ page }) => {
	// Unique per run so a reused database never trips the unique SKU/bin rules.
	const run = Date.now().toString();
	const sku = `E2E-${run}`;
	const name = `E2E Item ${run}`;
	const aisle = run.slice(-6);

	await page.goto('/');
	await page.fill('#username', username);
	await page.fill('#password', password);
	await page.getByRole('button', { name: /CONTINUE/ }).click();
	await page.waitForURL((url) => url.pathname !== '/');

	// A bin has to exist before anything can be received into it.
	await page.goto('/bins');
	await page.click('#add-bin-trigger');
	await page.fill('#zone', 'E2E');
	await page.fill('#aisle', aisle);
	await page.fill('#shelf', '1');
	await page.getByRole('button', { name: 'Save Bin' }).click();

	await page.goto('/inventory');
	await page.click('#add-item-trigger');
	await page.fill('#sku', sku);
	await page.fill('#name', name);
	await page.fill('#category', 'E2E');
	await page.fill('#unit-of-measure', 'unit');
	await page.getByRole('button', { name: 'Save Item' }).click();

	const row = page.getByRole('row', { name: new RegExp(sku) });
	await expect(row).toBeVisible();
	await expect(row.getByRole('cell').nth(4)).toHaveText('0');

	await page.goto('/employee');
	await page.selectOption('#item-id', {
		value: await page.locator('#item-id option', { hasText: sku }).getAttribute('value')
	});
	await page.selectOption('#bin-id', { label: `E2E-${aisle}-1` });
	await page.fill('#quantity', '7');
	await page.getByRole('button', { name: 'PROCESS ARRIVAL' }).click();
	await expect(page.locator('.alert-success')).toBeVisible();

	await page.goto('/inventory');
	await expect(
		page
			.getByRole('row', { name: new RegExp(sku) })
			.getByRole('cell')
			.nth(4)
	).toHaveText('7');
});
