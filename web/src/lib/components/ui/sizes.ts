/**
 * How tall a control is, by size, for everything you can press or type into.
 *
 * Buttons, inputs, selects and segmented controls each worked their height out from padding
 * and line-height, which meant they never agreed: a toolbar came out with a 36px segmented
 * control beside a 39px select beside 43px buttons, and a playlist header put a 34px labelled
 * button next to a 32px icon-only one. Even two Button variants differed, because only some of
 * them carry a border.
 *
 * So it is stated rather than derived. Everything in the set is `whitespace-nowrap` or a single
 * line, so a fixed height cannot clip anything.
 */
export type ControlSize = 'sm' | 'md';

export const CONTROL_HEIGHT: Record<ControlSize, string> = {
	sm: 'h-[34px]',
	md: 'h-[42px]'
};

/** The same heights as numbers, for the rare place that needs to match one in JS. */
export const CONTROL_HEIGHT_PX: Record<ControlSize, number> = {
	sm: 34,
	md: 42
};
