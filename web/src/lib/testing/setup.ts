// Matchers like toBeInTheDocument and toBeDisabled, for the component tests: they say what is
// being checked in the words somebody looking at the screen would use.
import '@testing-library/jest-dom/vitest';

// Unmounts whatever each test rendered. The testing plugin registers this itself through
// setupFiles, and naming a setup file of our own replaces that — which left one test's toast on
// the page for the next to find.
import '@testing-library/svelte/vitest';
