// DTO shapes mirroring the API's Contracts (the subset the web app consumes).
// Hand-written for now; an OpenAPI-generated client in shared/ is a later option.

export type Visibility = 'Private' | 'Unlisted' | 'Public';

export interface User {
	userId: string;
	username: string | null;
	email: string | null;
	authMethod: string;
	scopes: string[];
	showNsfw: boolean;
}

export type NsfwSetting = 'Auto' | 'Yes' | 'No';

export interface Playlist {
	id: string;
	name: string;
	slug: string;
	description: string | null;
	visibility: Visibility;
	itemCount: number;
	creationTime: string;
	tags: string[];
	nsfw: boolean;
	/** The folder this playlist is filed in for the current user (null = unfiled / root). */
	folderId: string | null;
	folderName: string | null;
	/** Whether the owner set the adult flag by hand. Null in listings, which don't report it. */
	nsfwSetting?: NsfwSetting | null;
	/** Links added but not yet fetched, so not yet listed or counted. */
	pendingCount?: number | null;
}

export interface Paged<T> {
	items: T[];
	nextCursor: string | null;
	total?: number;
}

export interface TagSummary {
	name: string;
	playlistCount: number;
}

/** A private folder node. The tree is built client-side from `parentId`. */
export interface Folder {
	id: string;
	name: string;
	parentId: string | null;
	subfolderCount: number;
	playlistCount: number;
	creationTime: string;
}

export interface FolderBreadcrumb {
	id: string;
	name: string;
}

/** A playlist filed in a folder — either the user's own or a saved public one. */
export interface FolderPlaylistEntry {
	playlistId: string;
	name: string;
	slug: string;
	description: string | null;
	visibility: Visibility;
	itemCount: number;
	tags: string[];
	nsfw: boolean;
	ownedByMe: boolean;
	ownerUsername: string;
}

export interface FolderDetail {
	id: string;
	name: string;
	parentId: string | null;
	breadcrumbs: FolderBreadcrumb[];
	subfolders: Folder[];
	playlists: FolderPlaylistEntry[];
}

export interface PublicPlaylistSummary {
	ownerUsername: string;
	slug: string;
	name: string;
	description: string | null;
	itemCount: number;
	creationTime: string;
	tags: string[];
	nsfw: boolean;
}

export type SourceType = 'Rss' | 'Scraper' | 'JsonApi';
export type SourceVisibility = 'Private' | 'Shared';
export type SourceRunStatus = 'Running' | 'Succeeded' | 'Failed';
export type SourceStatus = 'Active' | 'Paused' | 'Failing';

export interface LinkSummary {
	id: string;
	url: string;
	host: string;
	title: string | null;
	description: string | null;
	thumbnailUrl: string | null;
	siteName: string | null;
	enriched: boolean;
	nsfw: boolean;
	/** The site's favicon, shared by every link on that host. Null until a link there is enriched. */
	favicon: string | null;
	/** How the last fetch went. */
	enrichmentStatus: EnrichmentStatus;
	/** Why the last fetch failed, phrased for a reader. Null when it didn't. */
	enrichmentError: string | null;
}

export type EnrichmentStatus = 'Pending' | 'Succeeded' | 'Failed' | 'Broken';

export interface PlaylistItem {
	id: string;
	position: number;
	note: string | null;
	status: string;
	/** When the status last changed; null if it never has. */
	statusChangedAt?: string | null;
	link: LinkSummary;
	creationTime: string;
	metadata: Record<string, string> | null;
	sourceId: string | null;
	score: number | null;
}

export interface LinkPreview {
	canonicalUrl: string;
	host: string;
	title: string | null;
	description: string | null;
	imageUrl: string | null;
	siteName: string | null;
}

/** Subset of the API's SourceResponse the web app reads. */
export interface SourceSummary {
	id: string;
	name: string;
	type: SourceType;
	visibility: SourceVisibility;
	playlistIds: string[];
}

export interface SharedSource {
	id: string;
	name: string;
	type: SourceType;
	ownerUsername: string;
}

export interface AttachedSource {
	id: string;
	name: string;
	type: SourceType;
	ownerUsername: string;
	visibility: SourceVisibility;
	ownedByMe: boolean;
}

export interface Source {
	id: string;
	name: string;
	type: SourceType;
	config: Record<string, string>;
	schedule: string;
	visibility: SourceVisibility;
	lastRunAt: string | null;
	lastRunStatus: SourceRunStatus | null;
	creationTime: string;
	playlistIds: string[];
	/** Stopped sources keep their schedule but are unscheduled; "run now" still works. */
	status: SourceStatus;
	/** Failures since the last success. A source stops itself once this hits the threshold. */
	consecutiveFailures: number;
	/** IANA zone the schedule is read in; null means UTC. */
	timeZone: string | null;
}

export interface SourceRun {
	id: string;
	startedAt: string;
	finishedAt: string | null;
	status: string;
	/** Up to 20 of the URLs, kept for inspection — see foundCount for the real total. */
	itemsFound: string[];
	itemsAdded: string[];
	foundCount: number;
	addedCount: number;
	error: string | null;
}


export interface Quota {
	maxSources: number;
	sourcesUsed: number;
	maxRunsPerDay: number;
	runsUsedToday: number;
	maxItemsPerRun: number;
}

export interface ApiKey {
	id: string;
	name: string;
	prefix: string;
	scopes: string[];
	creationTime: string;
	lastUsedAt: string | null;
	expiresAt: string | null;
}

/** Returned once at creation — the only time the full token is exposed. */
export interface ApiKeyCreated {
	id: string;
	name: string;
	prefix: string;
	token: string;
	scopes: string[];
	expiresAt: string | null;
}

export interface ImportResult {
	imported: number;
	skipped: number;
	errors: string[];
}

export interface TrashedPlaylist {
	id: string;
	name: string;
	slug: string;
	itemCount: number;
	deletedAt: string;
	purgeAfter: string;
}

export interface TrashedItem {
	id: string;
	playlistId: string;
	playlistName: string;
	url: string;
	title: string | null;
	deletedAt: string;
	purgeAfter: string;
}

export interface Trash {
	playlists: TrashedPlaylist[];
	items: TrashedItem[];
	/** How long a deleted row stays restorable before it is purged. */
	retentionDays: number;
}

export interface SearchHit {
	itemId: string;
	playlistId: string;
	playlistName: string;
	link: LinkSummary;
	note: string | null;
	status: string;
	score: number | null;
	addedAt: string;
	/** When the status last changed; null if it never has. */
	statusChangedAt: string | null;
}

/** A site the caller saves from, and how many of their links are on it. */
export interface HostFacet {
	hostname: string;
	itemCount: number;
}

/** A user as seen from the outside: what they published, and nothing personal. */
export interface PublicProfile {
	username: string;
	joinedAt: string;
	publicPlaylistCount: number;
	publicItemCount: number;
}
