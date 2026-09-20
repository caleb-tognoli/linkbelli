// Matchers like toBeInTheDocument and toBeDisabled, for the component tests: they say what is
// being checked in the words somebody looking at the screen would use.
import '@testing-library/jest-dom/vitest';

// Unmounts whatever each test rendered. The testing plugin registers this itself through
// setupFiles, and naming a setup file of our own replaces that — which left one test's toast on
// the page for the next to find.
import '@testing-library/svelte/vitest';

/**
 * The Web Animations API, enough of it for a transition to run and finish.
 *
 * Svelte 5 runs `transition:` through element.animate, and jsdom has neither that nor
 * getAnimations — so a component with any transition on it threw before the test could look at
 * it. The stub finishes immediately, which is also what the tests want: they assert on where
 * something ended up, not on the frames in between.
 */
if (typeof Element !== 'undefined' && !Element.prototype.animate) {
	Element.prototype.animate = function animate() {
		let onfinish: (() => void) | null = null;
		const animation = {
			currentTime: 0,
			playState: 'finished',
			startTime: 0,
			effect: null,
			finished: Promise.resolve(),
			cancel() {},
			finish() {
				onfinish?.();
			},
			pause() {},
			play() {},
			reverse() {},
			addEventListener() {},
			removeEventListener() {},
			get onfinish() {
				return onfinish;
			},
			set onfinish(handler: (() => void) | null) {
				onfinish = handler;
				// Svelte waits for this to know the transition is over.
				if (handler) queueMicrotask(handler);
			}
		};
		return animation as unknown as Animation;
	};

	Element.prototype.getAnimations = function getAnimations() {
		return [];
	};
}
