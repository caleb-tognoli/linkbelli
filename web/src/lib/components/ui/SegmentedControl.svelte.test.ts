import { render, screen } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import SegmentedControl from './SegmentedControl.svelte';

const options = [
	{ value: 'all', label: 'All' },
	{ value: 'unwatched', label: 'Unwatched' },
	{ value: 'watched', label: 'Watched' }
];

describe('SegmentedControl', () => {
	it('is a radio group that says which option is chosen', () => {
		render(SegmentedControl, { label: 'Status', options, value: 'unwatched' });

		expect(screen.getByRole('radiogroup', { name: 'Status' })).toBeInTheDocument();
		expect(screen.getByRole('radio', { name: 'Unwatched' })).toHaveAttribute('aria-checked', 'true');
		expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'false');
	});

	it('moves the choice with the arrow keys, keeping one tab stop', async () => {
		const onchange = vi.fn();
		render(SegmentedControl, { label: 'Status', options, value: 'all', onchange });

		screen.getByRole('radio', { name: 'All' }).focus();
		await userEvent.keyboard('{ArrowRight}');

		expect(onchange).toHaveBeenCalledWith('unwatched');
		expect(screen.getByRole('radio', { name: 'Unwatched' })).toHaveFocus();
		expect(screen.getByRole('radio', { name: 'Unwatched' })).toHaveAttribute('tabindex', '0');
		expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('tabindex', '-1');
	});

	it('wraps around from the last option to the first', async () => {
		const onchange = vi.fn();
		render(SegmentedControl, { label: 'Status', options, value: 'watched', onchange });

		screen.getByRole('radio', { name: 'Watched' }).focus();
		await userEvent.keyboard('{ArrowRight}');

		expect(onchange).toHaveBeenCalledWith('all');
	});

	it('renders links, the current one marked, when options are addresses', () => {
		render(SegmentedControl, {
			label: 'Sort',
			options: [
				{ value: '', label: 'Newest', href: '/discover' },
				{ value: 'liked', label: 'Most liked', href: '/discover?sort=liked' }
			],
			value: 'liked'
		});

		expect(screen.getByRole('navigation', { name: 'Sort' })).toBeInTheDocument();
		expect(screen.getByRole('link', { name: 'Most liked' })).toHaveAttribute('aria-current', 'page');
	});
});
