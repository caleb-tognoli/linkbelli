// App-wide replacement for the browser's blocking confirm()/prompt() dialogs.
// A single GlobalDialog instance (mounted once in +layout.svelte) renders whatever
// is in `state`; these functions push a request and resolve when it closes.

type ConfirmState = {
	kind: 'confirm';
	message: string;
	danger?: boolean;
	confirmLabel?: string;
	resolve: (value: boolean) => void;
};

type PromptState = {
	kind: 'prompt';
	message: string;
	defaultValue: string;
	confirmLabel?: string;
	resolve: (value: string | null) => void;
};

type DialogState = ConfirmState | PromptState | null;

let state = $state<DialogState>(null);

export function getDialogState() {
	return state;
}

export function confirmDialog(
	message: string,
	opts?: { danger?: boolean; confirmLabel?: string }
): Promise<boolean> {
	return new Promise((resolve) => {
		state = { kind: 'confirm', message, danger: opts?.danger, confirmLabel: opts?.confirmLabel, resolve };
	});
}

export function promptDialog(
	message: string,
	defaultValue = '',
	opts?: { confirmLabel?: string }
): Promise<string | null> {
	return new Promise((resolve) => {
		state = { kind: 'prompt', message, defaultValue, confirmLabel: opts?.confirmLabel, resolve };
	});
}

export function resolveDialog(value: boolean | string | null) {
	if (!state) return;
	state.resolve(value as never);
	state = null;
}
