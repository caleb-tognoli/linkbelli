import { findExisting, isConfigured, listPlaylists, loadSettings, savePage, saveSettings } from './api.js';

const el = (id) => document.getElementById(id);

let settings;
let tab;

async function main() {
	settings = await loadSettings();
	[tab] = await chrome.tabs.query({ active: true, currentWindow: true });

	el('loading').hidden = true;

	if (!isConfigured(settings)) {
		el('unconfigured').hidden = false;
		el('open-options').addEventListener('click', () => chrome.runtime.openOptionsPage());
		return;
	}

	el('page-title').textContent = tab?.title ?? '';
	el('page-url').textContent = tab?.url ?? '';
	el('form').hidden = false;
	el('save').addEventListener('click', save);

	// The two lookups are independent, and neither should hold up the other: the playlist list is
	// what the person needs to act, and "already saved" is context they can read while choosing.
	await Promise.all([loadPlaylists(), showWhereAlreadySaved()]);
}

async function loadPlaylists() {
	try {
		const playlists = await listPlaylists(settings);
		const select = el('playlist');

		if (playlists.length === 0) {
			report('Make a playlist in Linkbelli first.', 'error');
			el('save').disabled = true;
			return;
		}

		for (const playlist of playlists) {
			const option = document.createElement('option');
			option.value = playlist.id;
			option.textContent = playlist.name;
			select.append(option);
		}

		// Whatever they chose last time, because people save to the same list repeatedly.
		if (settings.lastPlaylistId && playlists.some((p) => p.id === settings.lastPlaylistId)) {
			select.value = settings.lastPlaylistId;
		}
	} catch (error) {
		report(error.message, 'error');
		el('save').disabled = true;
	}
}

async function showWhereAlreadySaved() {
	if (!tab?.url) return;

	try {
		const existing = await findExisting(settings, tab.url);
		if (existing.length === 0) return;

		const names = existing.map((e) => e.playlistName).join(', ');
		const already = el('already');
		already.textContent = `Already saved in ${names}.`;
		already.hidden = false;
	} catch {
		// Context, not the job. If the lookup fails the person can still save.
	}
}

async function save() {
	const playlistId = el('playlist').value;
	if (!playlistId) return;

	el('save').disabled = true;
	report('Saving…', null);

	try {
		const { alreadySaved } = await savePage(settings, playlistId, tab.url, el('note').value.trim());

		await saveSettings({ lastPlaylistId: playlistId });
		report(alreadySaved ? 'Already in that playlist.' : 'Saved.', 'ok');

		// Long enough to read the result, short enough not to be in the way.
		setTimeout(() => window.close(), 900);
	} catch (error) {
		report(error.message, 'error');
		el('save').disabled = false;
	}
}

function report(text, kind) {
	const message = el('message');
	message.textContent = text;
	message.className = kind ?? '';
	message.hidden = !text;
}

main();
