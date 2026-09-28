# Auth UX — accessibility contract

Login and signup are where a job seeker first decides whether the product is
credible. These are requirements, not suggestions, and there are tests enforcing
them (`web/src/test/auth.test.tsx`).

## Focusable error summary

After a failed submit, an error summary appears above the form with
`role="alert"` and `tabindex="-1"`, and receives focus. Each entry is a link to
the field it refers to, and the inline error stays in place.

A toast is not sufficient — it is not keyboard reachable and screen reader users
often miss it entirely.

## Errors are never colour-only

Every error is rendered as text and wired with `aria-invalid` plus
`aria-describedby` pointing at the message. A red border alone tells a
colour-blind user nothing.

## Password fields stay usable

- No `onpaste` handler that blocks pasting. WCAG 2.2 AA calls this a critical
  failure — cognitive-function tests must not be the only way through.
- `autocomplete="current-password"` on login, `"new-password"` on signup, so
  password managers work.
- Password policy is never revealed on the login path. Stating it there would
  leak the current policy during account enumeration.

## Submit feedback

The button disables and reads "Working…" with `aria-busy="true"`. A button that
does nothing visible is a bug.

## Network failure is distinguishable

An unreachable API shows "We could not reach the server", separate from a
credential error. Users act differently for each.

## Account enumeration

Registration conflict (409) is the one place we reveal that an email exists,
because the person is creating that email. Login never distinguishes "no such
user" from "wrong password" — one identical message, and BCrypt runs against a
dummy hash on the miss path so response timing does not leak it either.

## Visual system

From `docs/DESIGN.md`. No gradients. Flat surfaces, hairline borders, and
crimson `#e11d48` reserved for the primary action and focus rings. Hierarchy
comes from type weight and size across four ink steps, not from colour.

All interactive targets are at least 44px tall. Focus is never removed without a
visible replacement.
