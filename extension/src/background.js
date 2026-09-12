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

/**
 * Marks whether the current tab is already saved, so the icon answers before it is clicked.
 *
 * Off unless it is asked for, and this is why: it runs on every completed navigation in every
 * tab, and each one is a request to the server carrying the full address. Left on, the server's
 * request log becomes a complete history of everything the person browsed — for a checkmark.
 *
 * When it is on: answers are cached per canonical address for the session, so revisiting a page
 * or moving between tabs costs nothing, and a busy hour does not walk into the rate limiter.
 */
const badgeCache = new Map();

/** A ceiling on the cache, so a long session cannot grow it without bound. */
const BADGE_CACHE_MAX = 500;

/** The address without the parts that identify a visit rather than a page. */
function badgeKey(url) {
	try {
		const parsed = new URL(url);
		return parsed.origin + parsed.pathname;
	} catch {
		return url;
	}
}

chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
	if (changeInfo.status !== 'complete' || !tab.url?.startsWith('http')) return;

	// Incognito is somebody saying they do not want this recorded anywhere.
	if (tab.incognito) return;

	const settings = await loadSettings();
	if (!isConfigured(settings) || !settings.checkVisited) return;

	const key = badgeKey(tab.url);
	if (badgeCache.has(key)) {
		await setBadge(tabId, badgeCache.get(key));
		return;
	}

	try {
		const existing = await findExisting(settings, tab.url);
		const saved = existing.length > 0;

		if (badgeCache.size >= BADGE_CACHE_MAX) badgeCache.clear();
		badgeCache.set(key, saved);

		await setBadge(tabId, saved);
	} catch {
		// A badge is a nicety; never let it surface as an error.
		await setBadge(tabId, false);
	}
});

async function setBadge(tabId, saved) {
	await chrome.action.setBadgeText({ tabId, text: saved ? '✓' : '' });
	if (saved) await chrome.action.setBadgeBackgroundColor({ tabId, color: '#2563eb' });
}

const BADGES = { ok: '✓', dup: '=', err: '!' };

async function flash(tabId, kind) {
	if (tabId === undefined) return;

	await chrome.action.setBadgeText({ tabId, text: BADGES[kind] });
	await chrome.action.setBadgeBackgroundColor({
		tabId,
		color: kind === 'err' ? '#dc2626' : '#2563eb'
	});
}
