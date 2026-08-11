export function looksLikeUrl(s: string): boolean {
	const t = s.trim();
	if (!/^https?:\/\//i.test(t)) return false;
	try {
		return !!new URL(t).host;
	} catch {
		return false;
	}
}
