/** The app's name, spelled the way it is spelled. */
export const BRAND = 'Linkbelli';

/**
 * What goes in the tab.
 *
 * Every page wrote its own "Page - linkbelli", in lower case, with a hyphen — so the product
 * named itself in a way that appears nowhere else in the interface, on every tab, bookmark and
 * search result.
 */
export function pageTitle(page?: string | null): string {
	const name = page?.trim();
	return name ? `${name} · ${BRAND}` : BRAND;
}
