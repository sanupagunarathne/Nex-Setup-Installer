# Smooth scrolling correction

Branch: `fix/smooth-scrolling`. Source investigation is retained separately on
`bug/abrupt-scrolling` (not merged into dev).

Lists now use pixel scrolling with virtualization retained. A shared window
behavior animates vertical mouse-wheel movement over 140 ms with easing. Rapid
wheel input accumulates toward a clamped destination, while direction changes
start from the visible position. At an inner viewport edge, a scrollable ancestor
is tried. Windows wheel-line/page settings and disabled client-area animations
are respected. Modifier-wheel gestures, logical scroll providers other than the
configured virtualized lists, keyboard navigation, touch and scrollbar dragging
retain native behavior. Keyboard, mouse press, touch and window unloading cancel
pending animations. No package execution or installation logic changes.

Validation: .NET restore, Release WPF cross-build and unit tests on Linux.
Regression tests cover bounds, rapid input, direction reversal, partial wheel
notches, disabled wheel scrolling and page-sized movement. They validate scroll
math, not actual Windows routed input or rendering.

Windows acceptance remains required: test long Apps/Fonts lists, nested install
panels and reports at their edges, fast wheel reversal, scrollbar dragging during
animation, keyboard navigation, 150/200% scaling, reduced animation settings,
touchpad gestures, and navigation while a transition is active. Keep Clear list
placement as a separate previously requested Phase 6 change.
