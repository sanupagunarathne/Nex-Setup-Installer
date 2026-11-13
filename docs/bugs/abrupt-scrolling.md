# Abrupt and trapped mouse-wheel scrolling

Source inspection: list controls use WPF's default item-based scrolling. Nested
scroll viewers (notably the install list and report fields) can consume wheel
input at their limits, preventing the surrounding page from moving. No wheel
animation currently exists. Native Windows reproduction is still required:
scroll a long Apps/Fonts list, then scroll beyond a nested panel's bottom edge.

Correction: pixel-based virtualized lists, short eased wheel transitions that
accumulate rapid input, and deliberate handoff to a scrollable ancestor at an
edge. Preserve native keyboard, scrollbar, text editing and reduced-motion
behavior. Test target clamping and reversal without loading WPF on Linux.
