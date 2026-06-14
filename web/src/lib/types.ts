// DTO shapes mirroring the API's Contracts (the subset the web app consumes).
// Hand-written for now; an OpenAPI-generated client in shared/ is a later option.

export type Visibility = 'Private' | 'Unlisted' | 'Public';

export interface User {
	userId: string;
	authMethod: string;
	scopes: string[];
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
}

export interface Paged<T> {
	items: T[];
	nextCursor: string | null;
}
