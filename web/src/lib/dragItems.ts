/**
 * The payload carried when items are dragged towards another playlist.
 *
 * Kept as JSON on a custom MIME type rather than `text/plain`: a drag that lands somewhere
 * unexpected — a text field, another tab, the desktop — should do nothing rather than paste a
 * line of machine detail.
 */
export const ITEMS_MIME = 'application/x-linkbelli-items';

export interface ItemDragPayload {
	/** The playlist the items are being taken out of. */
	fromPlaylistId: string;
	itemIds: string[];
	/** What to say while dragging, e.g. "3 links" or the title of the one being moved. */
	label: string;
}

/** What a drop does. Copy leaves the originals where they are. */
export type DropMode = 'move' | 'copy';

/**
 * Which items a drag should carry.
 *
 * Dragging a row that is part of the current selection takes the whole selection — anything else
 * would silently move one item out of forty that were visibly checked. Dragging a row outside the
 * selection takes just that row, because reaching for an unchecked thing is not a statement about
 * the checked ones.
 */
export function dragSet(draggedId: string, selected: Iterable<string>): string[] {
	const set = [...selected];

	return set.includes(draggedId) ? set : [draggedId];
}

/**
 * Whether the pointer's modifiers mean "copy".
 *
 * Ctrl on Windows and Linux, Option on a Mac — the conventions people's fingers already know.
 * Meta is included because Chrome on a Mac reports Cmd-drag this way.
 */
export function dropMode(e: { ctrlKey?: boolean; altKey?: boolean; metaKey?: boolean }): DropMode {
	return e.ctrlKey || e.altKey || e.metaKey ? 'copy' : 'move';
}

/** The bulk action a drop turns into. */
export function bulkActionFor(mode: DropMode): 'Move' | 'Copy' {
	return mode === 'copy' ? 'Copy' : 'Move';
}

export function encodePayload(payload: ItemDragPayload): string {
	return JSON.stringify(payload);
}

/**
 * Reads a payload back, or null when this drag is not one of ours.
 *
 * A drop handler sees whatever the operating system hands it — a file, a URL from another
 * application, a fragment of text. None of those should be treated as a set of item ids.
 */
export function decodePayload(raw: string | null | undefined): ItemDragPayload | null {
	if (!raw) return null;

	try {
		const parsed = JSON.parse(raw) as unknown;
		if (typeof parsed !== 'object' || parsed === null) return null;

		const { fromPlaylistId, itemIds, label } = parsed as Record<string, unknown>;
		if (typeof fromPlaylistId !== 'string' || !fromPlaylistId) return null;
		if (!Array.isArray(itemIds) || itemIds.length === 0) return null;
		if (!itemIds.every((id) => typeof id === 'string' && id.length > 0)) return null;

		return {
			fromPlaylistId,
			itemIds: itemIds as string[],
			label: typeof label === 'string' ? label : `${itemIds.length} links`
		};
	} catch {
		return null;
	}
}

/** What the drag is carrying, in words, for the label that follows the cursor. */
export function describeDrag(count: number, firstTitle: string | null | undefined): string {
	if (count === 1) return firstTitle?.trim() || '1 link';

	return `${count} links`;
}

/**
 * What a drop reported, phrased for a person rather than as two numbers.
 *
 * Skipped is the interesting half: for a copy or move it means the link was already in the target,
 * which is not an error but is the whole answer when it applies to everything dragged.
 */
export function describeResult(
	mode: DropMode,
	affected: number,
	skipped: number,
	target: string
): string {
	const verb = mode === 'copy' ? 'Copied' : 'Moved';

	if (affected === 0) {
		return skipped > 0 ? `Already in ${target}.` : `Nothing to ${mode}.`;
	}

	const links = `${affected} link${affected === 1 ? '' : 's'}`;
	const tail = skipped > 0 ? ` · ${skipped} already there` : '';

	return `${verb} ${links} to ${target}.${tail}`;
}
