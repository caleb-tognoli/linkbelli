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
}

export interface Paged<T> {
	items: T[];
	nextCursor: string | null;
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
}

export interface PlaylistItem {
	id: string;
	position: number;
	note: string | null;
	status: string;
	link: LinkSummary;
	creationTime: string;
	metadata: Record<string, string> | null;
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
	creationTime: string;
	playlistIds: string[];
}

export interface SourceRun {
	id: string;
	startedAt: string;
	finishedAt: string | null;
	status: string;
	itemsFound: string[];
	itemsAdded: string[];
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

