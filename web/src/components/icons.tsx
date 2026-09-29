/**
 * Icons. Inline SVG rather than emoji or an icon font, so they inherit
 * `currentColor`, need no network request, and carry an accessible name only
 * when the icon is the sole content of a control.
 *
 * Every icon takes `title` only when it is meaningful on its own; decorative
 * icons should be rendered with `aria-hidden` instead.
 */

type IconProps = {
  className?: string
  title?: string
}

function Svg({
  children,
  className = 'h-5 w-5',
  title,
}: IconProps & { children: React.ReactNode }) {
  const labelled = Boolean(title)
  return (
    <svg
      viewBox="0 0 24 24"
      fill="currentColor"
      className={className}
      role={labelled ? 'img' : undefined}
      aria-hidden={labelled ? undefined : true}
      focusable="false"
    >
      {labelled && <title>{title}</title>}
      {children}
    </svg>
  )
}

export function HomeIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8h5Z" />
    </Svg>
  )
}

export function SearchIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M15.5 14h-.79l-.28-.27a6.5 6.5 0 1 0-.7.7l.27.28v.79l5 4.99L20.49 19l-4.99-5Zm-6 0A4.5 4.5 0 1 1 14 9.5 4.5 4.5 0 0 1 9.5 14Z" />
    </Svg>
  )
}

export function BriefcaseIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M20 6h-4V4a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2H4a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2Zm-6 0h-4V4h4v2Z" />
    </Svg>
  )
}

export function UserIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 12a5 5 0 1 0-5-5 5 5 0 0 0 5 5Zm0 2c-4 0-8 2-8 5v3h16v-3c0-3-4-5-8-5Z" />
    </Svg>
  )
}

export function DocumentIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Zm-1 7V3.5L19.5 9ZM8 13h8v2H8Zm0 4h8v2H8Zm0-8h3v2H8Z" />
    </Svg>
  )
}

export function BellIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 22a2 2 0 0 0 2-2h-4a2 2 0 0 0 2 2Zm6-6v-5a6 6 0 0 0-5-5.91V4a1 1 0 0 0-2 0v1.09A6 6 0 0 0 6 11v5l-2 2v1h16v-1Z" />
    </Svg>
  )
}

export function ChevronDownIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M7 10l5 5 5-5H7Z" />
    </Svg>
  )
}

export function ChevronRightIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 18l6-6-6-6v12Z" />
    </Svg>
  )
}

export function MapPinIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 2a7 7 0 0 0-7 7c0 5.25 7 13 7 13s7-7.75 7-13a7 7 0 0 0-7-7Zm0 9.5A2.5 2.5 0 1 1 14.5 9 2.5 2.5 0 0 1 12 11.5Z" />
    </Svg>
  )
}

export function UploadIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 16h6v-6h4l-7-7-7 7h4v6Zm-4 2h14v2H5Z" />
    </Svg>
  )
}

export function CheckIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4Z" />
    </Svg>
  )
}

export function AlertIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M1 21h22L12 2Zm12-3h-2v-2h2Zm0-4h-2v-4h2Z" />
    </Svg>
  )
}

export function SparkIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="m12 2 2.4 6.2L21 10l-5.4 3.4L14 21l-4.2-5.6L4 13l5.4-3.4Z" />
    </Svg>
  )
}

/** The four suite destinations that the top nav could not hold. Left-rail
    specific: a 20px glyph with a 24px grid, matched to the existing set. */
export function TargetIcon({ className = 'h-5 w-5', title }: IconProps) {
  const labelled = Boolean(title)
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden={labelled ? undefined : true} role={labelled ? 'img' : undefined} focusable="false">
      {labelled && <title>{title}</title>}
      <path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 3.2a6.8 6.8 0 1 1 0 13.6 6.8 6.8 0 0 1 0-13.6Zm0 3.4a3.4 3.4 0 1 0 0 6.8 3.4 3.4 0 0 0 0-6.8Z" />
    </svg>
  )
}

export function ChatIcon({ className = 'h-5 w-5', title }: IconProps) {
  const labelled = Boolean(title)
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden={labelled ? undefined : true} role={labelled ? 'img' : undefined} focusable="false">
      {labelled && <title>{title}</title>}
      <path d="M4 3h16a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H8.6L4 21.5V19a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Zm2 5v2h12V8H6Zm0 4v2h8v-2H6Z" />
    </svg>
  )
}

export function BoardIcon({ className = 'h-5 w-5', title }: IconProps) {
  const labelled = Boolean(title)
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden={labelled ? undefined : true} role={labelled ? 'img' : undefined} focusable="false">
      {labelled && <title>{title}</title>}
      <path d="M3 4h5v16H3V4Zm6.5 0h5v10h-5V4Zm6.5 0h5v13h-5V4Z" />
    </svg>
  )
}

export function LifeBuoyIcon({ className = 'h-5 w-5', title }: IconProps) {
  const labelled = Boolean(title)
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden={labelled ? undefined : true} role={labelled ? 'img' : undefined} focusable="false">
      {labelled && <title>{title}</title>}
      <path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 3a7 7 0 0 1 3.9 1.2l-2.3 2.3a3.6 3.6 0 0 0-3.2 0L8.1 6.2A7 7 0 0 1 12 5Zm-7 7a7 7 0 0 1 1.2-3.9l2.3 2.3a3.6 3.6 0 0 0 0 3.2L6.2 15.8A7 7 0 0 1 5 12Zm7 7a7 7 0 0 1-3.9-1.2l2.3-2.3a3.6 3.6 0 0 0 3.2 0l2.3 2.3A7 7 0 0 1 12 19Zm4.8-3.2-2.3-2.3a3.6 3.6 0 0 0 0-3.2l2.3-2.3A7 7 0 0 1 12 19" />
    </svg>
  )
}

export function MenuIcon({ className = 'h-5 w-5', title }: IconProps) {
  const labelled = Boolean(title)
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden={labelled ? undefined : true} role={labelled ? 'img' : undefined} focusable="false">
      {labelled && <title>{title}</title>}
      <path d="M3 6h18v2.5H3V6Zm0 4.75h18v2.5H3v-2.5ZM3 15.5h18V18H3v-2.5Z" />
    </svg>
  )
}

export function CloseIcon({ className = 'h-5 w-5', title }: IconProps) {
  const labelled = Boolean(title)
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden={labelled ? undefined : true} role={labelled ? 'img' : undefined} focusable="false">
      {labelled && <title>{title}</title>}
      <path d="m12 10.6 5-5 1.4 1.4-5 5 5 5-1.4 1.4-5-5-5 5L5.6 17l5-5-5-5L7 5.6l5 5Z" />
    </svg>
  )
}

export function ShieldIcon({ className = 'h-5 w-5', title }: IconProps) {
  const labelled = Boolean(title)
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden={labelled ? undefined : true} role={labelled ? 'img' : undefined} focusable="false">
      {labelled && <title>{title}</title>}
      <path d="M12 2 4 5v6.5c0 4.7 3.2 9 8 10.5 4.8-1.5 8-5.8 8-10.5V5l-8-3Zm0 2.1 6 2.2V11c0 3.6-2.3 6.9-6 8.2-3.7-1.3-6-4.6-6-8.2V6.3l6-2.2Z" />
    </svg>
  )
}
