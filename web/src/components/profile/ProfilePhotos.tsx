import { useRef, useState, type ChangeEvent } from 'react'
import { profile } from '../../lib/api'
import { initials } from '../../lib/profile'

/**
 * Banner + avatar pair, styled after the LinkedIn profile sheet: a wide banner
 * with the square avatar overlapping its bottom edge. Uploads persist
 * immediately (photos apply the moment they are picked); the text fields around
 * them ride the main Save.
 */
export function ProfilePhotos({
  bannerPicture,
  profilePicture,
  photoConsentGiven,
  onMedia,
  onConsent,
}: {
  bannerPicture: string | null
  profilePicture: string | null
  photoConsentGiven: boolean
  onMedia: (p: { profilePicture: string | null; bannerPicture: string | null }) => void
  onConsent: (given: boolean) => void
}) {
  const [busy, setBusy] = useState<'picture' | 'banner' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const bannerRef = useRef<HTMLInputElement>(null)
  const pictureRef = useRef<HTMLInputElement>(null)

  const uploadField = async (kind: 'picture' | 'banner', file: File) => {
    setBusy(kind)
    setError(null)
    try {
      const res = await profile.uploadMedia(kind, file)
      onMedia(res)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not upload the photo.')
    } finally {
      setBusy(null)
    }
  }

  const pickBanner = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) void uploadField('banner', file)
    e.target.value = ''
  }

  const pickPicture = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) void uploadField('picture', file)
    e.target.value = ''
  }

  const bannerUrl = profile.mediaUrl(bannerPicture)
  const pictureUrl = profile.mediaUrl(profilePicture)

  return (
    <div>
      <div className="relative">
        <label className="relative block h-32 cursor-pointer overflow-hidden rounded-lg bg-surface-sunken md:h-40">
          {bannerUrl ? (
            <img src={bannerUrl} alt="Profile banner" className="h-full w-full object-cover" />
          ) : (
            <span className="absolute inset-0 flex items-center justify-center text-sm font-semibold text-ink-muted">
              Add a banner
            </span>
          )}
          <span
            aria-hidden="true"
            className={`absolute right-3 top-3 ${busy === 'banner' ? '' : 'hidden'}`}
          >
            <Spinner />
          </span>
          <input
            ref={bannerRef}
            type="file"
            accept="image/png,image/jpeg,image/webp"
            className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
            aria-label="Upload a banner image"
            onChange={pickBanner}
          />
        </label>

        <div className="absolute -bottom-10 left-4">
          <label className="relative block h-24 w-24 cursor-pointer overflow-hidden rounded-full border-4 border-surface bg-surface-sunken transition-opacity hover:opacity-90">
            {pictureUrl ? (
              <img
                src={pictureUrl}
                alt="Your profile photo"
                className="h-full w-full object-cover"
              />
            ) : (
              <span
                aria-hidden="true"
                className="flex h-full w-full items-center justify-center bg-brand text-xl font-extrabold text-white"
              >
                {initials(null)}
              </span>
            )}
            <input
              ref={pictureRef}
              type="file"
              accept="image/png,image/jpeg,image/webp"
              className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
              aria-label="Upload a profile photo"
              onChange={pickPicture}
            />
          </label>
        </div>
      </div>

      {/* Shared spacer reserved for the overlapping avatar. */}
      <div className="h-12" />

      <div className="space-y-3">
        <div className="flex flex-wrap gap-2">
          {profilePicture && (
            <RemoveMediaButton name={profilePicture} onRemoved={onMedia}>
              Remove photo
            </RemoveMediaButton>
          )}
          {bannerPicture && (
            <RemoveMediaButton name={bannerPicture} onRemoved={onMedia}>
              Remove banner
            </RemoveMediaButton>
          )}
        </div>

        {error && (
          <p role="alert" className="rounded-lg border border-danger bg-danger-subtle px-3 py-2 text-sm font-medium text-danger">
            {error}
          </p>
        )}

        <label className="flex min-h-11 cursor-pointer items-start gap-3 rounded-lg border border-border-subtle px-3 py-2.5 text-sm text-ink-body hover:bg-sunken">
          <input
            type="checkbox"
            checked={photoConsentGiven}
            onChange={(e) => onConsent(e.target.checked)}
            className="mt-1 h-4 w-4 accent-[var(--color-accent)]"
          />
          <span>
            <span className="font-semibold text-ink">Show my photo to employers</span>
            <span className="block text-ink-muted">
              Your photos stay private until you consent. Consent survives
              replacing the photo.
            </span>
          </span>
        </label>
      </div>
    </div>
  )
}

function RemoveMediaButton({
  name,
  onRemoved,
  children,
}: {
  name: string
  onRemoved: (p: { profilePicture: string | null; bannerPicture: string | null }) => void
  children: string
}) {
  const [busy, setBusy] = useState(false)
  const remove = async () => {
    setBusy(true)
    try {
      const res = await profile.removeMedia(name)
      onRemoved(res)
    } catch {
      /* A failed removal just leaves the photo in place. */
    } finally {
      setBusy(false)
    }
  }
  return (
    <button
      type="button"
      onClick={() => void remove()}
      disabled={busy}
      className="min-h-11 rounded-lg border border-border-strong px-3 py-2 text-sm font-semibold text-ink transition-colors hover:bg-sunken disabled:text-ink-faint"
    >
      {busy ? 'Removing…' : children}
    </button>
  )
}

function Spinner() {
  return (
    <span
      className="block h-5 w-5 animate-spin rounded-full border-2 border-white/40 border-t-white"
      role="status"
      aria-label="Uploading"
    />
  )
}