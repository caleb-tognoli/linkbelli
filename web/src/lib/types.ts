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
	/** Whether to ask the Internet Archive for a public snapshot of pages this user saves. */
	archiveLinks?: boolean;
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
	/** Mean of the scores given, or null when nothing here is rated. */
	averageScore?: number | null;
	/** How many items carry a score. */
	scoredCount?: number | null;
	/** How the caller last looked at this playlist; null when they have no saved view. */
	view?: PlaylistView | null;
}

/** How one person looks at one playlist. Saved per account, so it follows them between devices. */
export interface PlaylistView {
	sort: string | null;
	source: string | null;
	status: string | null;
	showUrls: boolean;
	showThumbnails: boolean;
	viewMode: string | null;
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
	/** Words in the article saved at enrichment; null when the page had no article in it. */
	wordCount?: number | null;
	/** What this link is. Unknown until the classifier has seen it. */
	kind?: ContentKind;
	/** A public snapshot of the page, when one is being kept. Null when there isn't one. */
	archiveUrl?: string | null;
}

export type ContentKind =
	| 'Unknown'
	| 'Article'
	| 'Video'
	| 'Repository'
	| 'Paper'
	| 'Document'
	| 'Audio'
	| 'Image'
	| 'Social';

/** The readable text of a saved page, as it was when it was saved. */
export interface LinkContent {
	id: string;
	url: string;
	host: string;
	title: string | null;
	siteName: string | null;
	paragraphs: string[];
	wordCount: number;
	/** Whether these paragraphs are only the start of the article. */
	truncated: boolean;
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
	/** Tags on the link itself, as opposed to on the playlist holding it. */
	tags?: string[];
	/** The token this item is shared under, or null when it isn't shared. */
	shareToken?: string | null;
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
	/** What this source may bring in; null accepts everything it finds. */
	filter: SourceFilter | null;
}

/**
 * What a source is allowed to bring in. Every field is optional, and an all-empty filter is
 * stored as none at all.
 */
export interface SourceFilter {
	/** Regular expressions, matched case-insensitively. */
	titleInclude: string | null;
	titleExclude: string | null;
	urlInclude: string | null;
	urlExclude: string | null;
	/** Hold back anything published more recently than this. Needs a date from the source. */
	minAgeHours: number | null;
	/** Cap per run, applied after the patterns. */
	maxItems: number | null;
	/** Days a removed item stays removed, instead of returning on the next run. */
	dedupeWindowDays: number | null;
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
	/** Links the source's filter turned away — by pattern, by age, or by the cap. */
	skippedCount: number;
	error: string | null;
}

/** A source's recent record, summarised from its run rows. */
export interface SourceHealth {
	/** Finished runs inside the window. Running ones are excluded — they have no outcome yet. */
	runs: number;
	windowDays: number;
	succeeded: number;
	failed: number;
	/** Null when nothing has run: a source nobody has used yet is not failing. */
	successRate: number | null;
	/** Averaged over successful runs only. Null when none succeeded. */
	averageFound: number | null;
	averageAdded: number | null;
	/** Runs that succeeded and found nothing — how a broken selector looks from the outside. */
	emptyRuns: number;
	consecutiveFailures: number;
	lastRunAt: string | null;
	lastRunStatus: SourceRunStatus | null;
	lastError: string | null;
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
	/** Tags on the link itself. */
	tags: string[];
	/**
	 * Where the term was found in the article text, when it was only found there. Null for a hit
	 * on the title or the note, where the reason it matched is already on screen.
	 */
	snippet: string | null;
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

export type DuplicateKind = 'SameLink' | 'SamePage';

export interface DuplicateCopy {
	itemId: string;
	playlistId: string;
	playlistName: string;
	url: string;
	title: string | null;
	addedAt: string;
}

/** A set of saved links that are the same thing — always two or more. */
export interface DuplicateGroup {
	kind: DuplicateKind;
	key: string;
	copies: DuplicateCopy[];
}

/** A search someone wants to come back to. What it matches is whatever matches now. */
export interface SavedSearch {
	id: string;
	name: string;
	q: string | null;
	host: string | null;
	tags: string[];
	itemTags: string[];
	status: string | null;
	minScore: number | null;
	broken: boolean;
	sort: string | null;
	creationTime: string;
	/** Restrict to one kind of thing. */
	kind: string | null;
	/** Only what can be read in this many minutes. */
	maxMinutes: number | null;
}

/** One value a source template asks the person for. */
export interface SourceTemplateField {
	key: string;
	label: string;
	placeholder: string | null;
	help: string | null;
	required: boolean;
}

/** A ready-made source config, with the hard parts already filled in. */
export interface SourceTemplate {
	id: string;
	key: string | null;
	name: string;
	description: string;
	type: SourceType;
	suggestedSchedule: string | null;
	builtin: boolean;
	fields: SourceTemplateField[];
}

/** The size and shape of what someone has here. */
export interface Usage {
	playlists: number;
	items: number;
	pendingItems: number;
	folders: number;
	sources: number;
	savedSearches: number;
	sites: number;
	watched: number;
	broken: number;
	inTrash: number;
}

/** One "when this arrives, do that" over your own collection. Conditions are all required. */
export interface AutomationRule {
	id: string;
	name: string;
	enabled: boolean;
	/** Rules run lowest first; one can move an item out from under the next. */
	position: number;
	playlistId: string | null;
	host: string | null;
	titlePattern: string | null;
	urlPattern: string | null;
	kind: ContentKind | null;
	addTags: string[];
	moveToPlaylistId: string | null;
	copyToPlaylistId: string | null;
	markWatched: boolean;
	trash: boolean;
	/** Stop after this one matches, so a specific rule can shield an item from a broad one. */
	stopOnMatch: boolean;
	/** How many items it has acted on — what makes a rule that never fires visible. */
	matchCount: number;
	lastMatchedAt: string | null;
	creationTime: string;
}

/** What a rule would have caught among what is already saved. */
export interface AutomationPreview {
	matches: number;
	sample: { itemId: string; playlistName: string; url: string; title: string | null }[];
}

/** One link someone sent you, as an anonymous visitor sees it. */
export interface SharedItem {
	url: string;
	host: string;
	title: string | null;
	description: string | null;
	/** The link id, for the thumbnail proxy. Null when the page had no image. */
	thumbnailLinkId: string | null;
	siteName: string | null;
	/** The sender's own note — usually the reason they sent it. */
	note: string | null;
	sharedBy: string;
	sharedAt: string;
	nsfw: boolean;
	kind: ContentKind;
	wordCount: number | null;
}
