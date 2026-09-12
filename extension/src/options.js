import { REQUIRED_SCOPES, listPlaylists, loadSettings, saveSettings } from './api.js';

const el = (id) => document.getElementById(id);

async function main() {
	const settings = await loadSettings();
	el('server').value = settings.serverUrl;
	el('key').value = settings.apiKey;
	el('check-visited').checked = settings.checkVisited;

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
	const checkVisited = el('check-visited').checked;

	// The extension no longer asks for every website up front — a bookmarking tool wanting
	// read-and-modify on all sites is a hard thing to justify at the install prompt, and it only
	// ever needed the one server. Asked for here instead, once there is an address to ask about.
	if (serverUrl && !(await ensureHostPermission(serverUrl))) {
		report('Saved, but the browser did not grant access to that address.', 'error');
		return;
	}

	await saveSettings({ serverUrl, apiKey, checkVisited });

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

/**
 * Asks the browser for access to this one server.
 *
 * Returns true when it is already granted, which it will be on every save after the first.
 * chrome.permissions.request has to be called from a user gesture, which the Save button is.
 */
async function ensureHostPermission(serverUrl) {
	let origins;
	try {
		origins = [new URL(serverUrl).origin + '/*'];
	} catch {
		return false;
	}

	if (await chrome.permissions.contains({ origins })) return true;

	return chrome.permissions.request({ origins });
}

function report(text, kind) {
	const message = el('message');
	message.textContent = text;
	message.className = kind ?? '';
	message.hidden = !text;
}

main();
