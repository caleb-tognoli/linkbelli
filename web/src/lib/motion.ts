/**
 * Whether the person has asked their system for less motion.
 *
 * CSS transitions are handled by one rule in app.css. This is for motion started from script —
 * a scroll that glides rather than jumps — which no stylesheet can reach.
 */
export function prefersReducedMotion(): boolean {
	return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** The scroll behaviour to ask for: a glide, unless less motion was asked for. */
export function scrollBehavior(): ScrollBehavior {
	return prefersReducedMotion() ? 'auto' : 'smooth';
}
