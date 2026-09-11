import { findExisting, isConfigured, loadSettings, savePage } from './api.js';

/** Right-click a link, or the page, and save it without opening the popup. */
const MENU_ID = 'linkbelli-save';

chrome.runtime.onInstalled.addListener(() => {
	chrome.contextMenus.create({
		id: MENU_ID,
		title: 'Save to Linkbelli',
		contexts: ['page', 'link']
	});
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
	if (info.menuItemId !== MENU_ID) return;

	// A link's own address when one was right-clicked, otherwise the page itself.
	const url = info.linkUrl ?? info.pageUrl ?? tab?.url;
	if (!url) return;

	const settings = await loadSettings();
	if (!isConfigured(settings) || !settings.lastPlaylistId) {
		// Nowhere to put it yet — send them to set it up rather than failing silently.
		await chrome.runtime.openOptionsPage();
		return;
	}

	try {
		const { alreadySaved } = await savePage(settings, settings.lastPlaylistId, url, null);
		await flash(tab?.id, alreadySaved ? 'dup' : 'ok');
	} catch {
		await flash(tab?.id, 'err');
	}
});

/** Marks whether the current tab is already saved, so the icon answers before it is clicked. */
chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
	if (changeInfo.status !== 'complete' || !tab.url?.startsWith('http')) return;

	const settings = await loadSettings();
	if (!isConfigured(settings)) return;

	try {
		const existing = await findExisting(settings, tab.url);
		await chrome.action.setBadgeText({ tabId, text: existing.length > 0 ? '✓' : '' });
		await chrome.action.setBadgeBackgroundColor({ tabId, color: '#2563eb' });
	} catch {
		// A badge is a nicety; never let it surface as an error.
		await chrome.action.setBadgeText({ tabId, text: '' });
	}
});

const BADGES = { ok: '✓', dup: '=', err: '!' };

async function flash(tabId, kind) {
	if (tabId === undefined) return;

	await chrome.action.setBadgeText({ tabId, text: BADGES[kind] });
	await chrome.action.setBadgeBackgroundColor({
		tabId,
		color: kind === 'err' ? '#dc2626' : '#2563eb'
	});
}
