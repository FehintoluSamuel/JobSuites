import type { CandidateProfile, ProfileCompleteness } from './api'

/** 36 states + FCT, matching the server taxonomy in ProfileTaxonomy. */
export const NIGERIAN_STATES = [
  'Abia', 'Adamawa', 'Akwa Ibom', 'Anambra', 'Bauchi', 'Bayelsa', 'Benue',
  'Borno', 'Cross River', 'Delta', 'Ebonyi', 'Edo', 'Ekiti', 'Enugu',
  'FCT Abuja', 'Gombe', 'Imo', 'Jigawa', 'Kaduna', 'Kano', 'Katsina',
  'Kebbi', 'Kogi', 'Kwara', 'Lagos', 'Nasarawa', 'Niger', 'Ogun', 'Ondo',
  'Osun', 'Oyo', 'Plateau', 'Rivers', 'Sokoto', 'Taraba', 'Yobe', 'Zamfara',
] as const

export const JOB_TYPES = ['full-time', 'contract', 'internship', 'part-time'] as const

export const LINK_LABELS = [
  'LinkedIn',
  'GitHub',
  'Portfolio',
  'X',
  'Website',
  'Other',
] as const

export const LANGUAGE_PROFICIENCIES = [
  'basic',
  'conversational',
  'professional',
  'native',
] as const

export interface CompletenessChecks {
  required: { label: string; done: boolean }[]
  optional: { label: string; done: boolean }[]
}

/**
 * Mirrors the server's ProfileCompleteness.Compute so the checklist updates as
 * the user types, not only after a save. Keeping the two in lockstep is worth
 * the duplication: a live progress bar is what makes "prompt them to fill
 * everything in" feel responsive.
 */
export function completenessChecks(p: Partial<Profile>): CompletenessChecks {
  const has = (v: string | null | undefined) => Boolean(v && v.trim().length > 0)
  const listHas = (v?: string[]) => Boolean(v && v.length > 0)

  const required = [
    { label: 'Your name', done: has(p.fullName) },
    { label: 'Contact email', done: has(p.email) },
    { label: 'Location', done: has(p.location) },
    { label: 'A CV to work from', done: Boolean(p.skills?.length || p.experiences?.length) },
    { label: 'Skills', done: Boolean(p.skills?.length) },
    { label: 'Years of experience', done: p.yearsExperience !== undefined && p.yearsExperience !== null },
    { label: 'Target roles', done: listHas(p.targetRoles) },
    { label: 'Preferred states', done: listHas(p.preferredStates) },
    { label: 'Desired job types', done: listHas(p.desiredJobTypes) },
  ]

  const optional = [
    { label: 'Headline', done: has(p.headline) },
    { label: 'Summary', done: has(p.summary) },
    { label: 'Phone', done: has(p.phone) },
    { label: 'Photos', done: Boolean(p.profilePicture || p.bannerPicture) },
    { label: 'Education', done: Boolean(p.education?.length) },
    { label: 'Certifications', done: Boolean(p.certifications?.length) },
    { label: 'Languages', done: Boolean(p.languages?.length) },
    { label: 'Professional links', done: Boolean(p.links?.length) },
    { label: 'Availability', done: has(p.availability) },
    { label: 'Desired salary', done: has(p.desiredSalary) },
  ]

  return { required, optional }
}

export function computeCompleteness(p: Partial<Profile>): ProfileCompleteness {
  const { required, optional } = completenessChecks(p)
  const requiredMet = required.filter((r) => r.done).length
  const optionalMet = optional.filter((r) => r.done).length
  return {
    percent: required.length === 0 ? 0 : Math.round((100 * requiredMet) / required.length),
    requiredMet,
    requiredTotal: required.length,
    optionalMet,
    optionalTotal: optional.length,
    missing: required.filter((r) => !r.done).map((r) => r.label),
  }
}

export type Profile = CandidateProfile

/** Local-editing model for the profile page: nullable strings, empty arrays. */
export function emptyProfile(): Profile {
  return {
    fileName: '',
    fullName: null,
    email: null,
    phone: null,
    location: null,
    headline: null,
    yearsExperience: null,
    summary: null,
    profilePicture: null,
    bannerPicture: null,
    photoConsentGiven: false,
    desiredSalary: null,
    availability: null,
    targetRoles: [],
    preferredStates: [],
    desiredJobTypes: [],
    skills: [],
    experiences: [],
    education: [],
    certifications: [],
    languages: [],
    links: [],
    completeness: {
      percent: 0,
      requiredMet: 0,
      requiredTotal: 9,
      optionalMet: 0,
      optionalTotal: 10,
      missing: [],
    },
    updatedAt: new Date(0).toISOString(),
  }
}

export function toEditable(p: CandidateProfile): Profile {
  return {
    ...p,
    skills: p.skills.map((s) => ({ ...s })),
    experiences: p.experiences.map((e) => ({ ...e })),
    education: p.education.map((e) => ({ ...e })),
    certifications: p.certifications.map((c) => ({ ...c })),
    languages: p.languages.map((l) => ({ ...l })),
    links: p.links.map((l) => ({ ...l })),
  }
}

/** Returning true when the field actually changed so the caller can decide
    whether to bump the dirty flag. */
export function profileChanged(a: Profile, b: Profile): boolean {
  return JSON.stringify(serializeForSave(a)) !== JSON.stringify(serializeForSave(b))
}

export function serializeForSave(p: Profile): CandidateProfile {
  return {
    ...p,
    skills: p.skills.map((s) => ({ ...s, source: s.source === 'manual' ? 'manual' : 'cv' })),
    experiences: p.experiences.map((e) => ({
      ...e,
      source: e.source === 'manual' ? 'manual' : 'cv',
    })),
    education: p.education.map((e) => ({ ...e, source: e.source === 'manual' ? 'manual' : 'cv' })),
    certifications: p.certifications.map((c) => ({ ...c, source: c.source === 'manual' ? 'manual' : 'cv' })),
    languages: p.languages.map((l) => ({ ...l, source: l.source === 'manual' ? 'manual' : 'cv' })),
    links: p.links.map((l) => ({ ...l, source: l.source === 'manual' ? 'manual' : 'cv' })),
  }
}

export function initials(name: string | null): string {
  const parts = (name ?? 'Profile').split(/\s+/).filter(Boolean).slice(0, 2)
  return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || 'CV'
}