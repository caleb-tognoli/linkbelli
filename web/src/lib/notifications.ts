/**
 * What an account has asked to be emailed about.
 *
 * In its own module so the settings page can fetch it with the rest of the page — the panel used
 * to ask for it after the page had painted, and showed the word "Looking…" in the meantime.
 */
export interface NotificationPrefs {
	onShare: boolean;
	onFollow: boolean;
	onSourceStopped: boolean;
	weeklyDigest: boolean;
}
