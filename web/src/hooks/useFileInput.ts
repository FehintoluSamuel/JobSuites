import { useRef } from 'react'

/**
 * A hidden <input type="file"> plus a trigger, so the visible control can be a
 * styled <button>. The input keeps focus off-screen (tabIndex -1) and always
 * resets its value so picking the same file twice still fires onChange.
 */
export function useFileInput({
  accept,
  onFile,
}: {
  accept: string
  onFile: (file: File) => void | Promise<void>
}) {
  const ref = useRef<HTMLInputElement>(null)

  const trigger = () => ref.current?.click()

  const inputProps = {
    ref,
    type: 'file' as const,
    accept,
    className: 'hidden',
    tabIndex: -1,
    'aria-hidden': true as const,
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0]
      if (file) void onFile(file)
      e.target.value = ''
    },
  }

  return { ref, trigger, inputProps }
}