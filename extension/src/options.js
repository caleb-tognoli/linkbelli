import { REQUIRED_SCOPES, listPlaylists, loadSettings, saveSettings } from './api.js';

const el = (id) => document.getElementById(id);

async function main() {
	const settings = await loadSettings();
	el('server').value = settings.serverUrl;
	el('key').value = settings.apiKey;

	for (const scope of REQUIRED_SCOPES) {
		const item = document.createElement('li');
		item.innerHTML = `<code>${scope}</code>`;
		el('scopes').append(item);
	}

	el('save').addEventListener('click', save);
}

async function save() {
	const serverUrl = el('server').value.trim().replace(/\/+$/, '');
	const apiKey = el('key').value.trim();

	await saveSettings({ serverUrl, apiKey });

	// Saving settings that don't work is worse than not saving: check them against the server so
	// the answer arrives here, not later in a popup with no room to explain.
	report('Checking…', null);
	try {
		await listPlaylists({ serverUrl, apiKey });
		report('Saved, and the key works.', 'ok');
	} catch (error) {
		report(`Saved, but: ${error.message}`, 'error');
	}
}

function report(text, kind) {
	const message = el('message');
	message.textContent = text;
	message.className = kind ?? '';
	message.hidden = !text;
}

main();
