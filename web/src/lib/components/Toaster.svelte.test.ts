import { render, screen } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { toast } from '$lib/toast.svelte';
import Toaster from './Toaster.svelte';

afterEach(() => {
	toast.clear();
	vi.useRealTimers();
});

describe('Toaster', () => {
	it('shows good news as a status and lets it go by itself', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		render(Toaster);

		toast.success('Saved.');
		expect(await screen.findByRole('status')).toHaveTextContent('Saved.');

		await vi.advanceTimersByTimeAsync(5100);
		expect(screen.queryByText('Saved.')).not.toBeInTheDocument();
	});

	it('announces a failure as an alert and keeps it until dismissed', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		render(Toaster);

		toast.error('Could not save that.');
		expect(await screen.findByRole('alert')).toHaveTextContent('Could not save that.');

		await vi.advanceTimersByTimeAsync(20_000);
		expect(screen.getByRole('alert')).toBeInTheDocument();

		await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
		expect(screen.queryByRole('alert')).not.toBeInTheDocument();
	});

	it('runs its action and goes', async () => {
		const run = vi.fn();
		render(Toaster);

		toast.success('Moved to trash.', { action: { label: 'Undo', run } });
		await userEvent.click(await screen.findByRole('button', { name: 'Undo' }));

		expect(run).toHaveBeenCalledOnce();
		expect(screen.queryByText('Moved to trash.')).not.toBeInTheDocument();
	});
});
