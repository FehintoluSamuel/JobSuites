import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, auth, tokenStore, type User } from '../lib/api'

interface AuthValue {
  user: User | null
  isLoading: boolean
  isAuthenticated: boolean
  login: (input: { email: string; password: string }) => Promise<void>
  register: (input: { email: string; fullName: string; password: string }) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()

  // The token itself is React state, not a localStorage read inside the query
  // options. Reading it directly made `enabled` a snapshot taken at render time:
  // signing out could not flip it, so a removed query was immediately rebuilt
  // and refetched by its still-mounted observer — one 401 per query, plus a
  // retry, which is what the user saw in the console.
  const [token, setToken] = useState<string | null>(() => tokenStore.get())

  // Runs whenever there is a token, and stays disabled when there is not — so
  // signed-out visitors never fire a doomed request.
  const { data, isLoading } = useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        const me = await auth.me()
        return { id: me.id, email: me.email, fullName: me.name, createdAt: '' }
      } catch (err) {
        // A rejected token is not an error state the user should see — it just
        // means they are signed out.
        if (err instanceof ApiError && err.isUnauthorized) {
          tokenStore.clear()
          setToken(null)
          return null
        }
        throw err
      }
    },
    enabled: Boolean(token),
    staleTime: 5 * 60_000,
    retry: false,
  })

  const clearSession = useCallback(() => {
    // Drop the token first, so `enabled: false` takes effect on the next render
    // before anything can refetch.
    tokenStore.clear()
    setToken(null)

    // Cancel anything still in flight, then drop the cache and the token so no
    // authenticated query can refire after sign-out.
    void queryClient.cancelQueries()
    queryClient.clear()

    // `clear()` empties the cache, but the `me` observer is still mounted and
    // would re-seed the cache from its own last-known data on the next render.
    // Seeding it with `null` is what actually flips `user` to null and lets the
    // route guard redirect to /login.
    queryClient.setQueryData(['me'], null)
  }, [queryClient])

  const applyAuth = useCallback(
    (result: { token: string; user: User }) => {
      tokenStore.set(result.token)
      setToken(result.token)
      queryClient.setQueryData(['me'], result.user)
    },
    [queryClient],
  )

  const loginMutation = useMutation({
    mutationFn: auth.login,
    onSuccess: applyAuth,
  })

  const registerMutation = useMutation({
    mutationFn: auth.register,
    onSuccess: applyAuth,
  })

  const value = useMemo<AuthValue>(
    () => ({
      user: data ?? null,
      isLoading,
      isAuthenticated: Boolean(data),
      login: async (input) => {
        await loginMutation.mutateAsync(input)
      },
      register: async (input) => {
        await registerMutation.mutateAsync(input)
      },
      logout: clearSession,
    }),
    [data, isLoading, loginMutation, registerMutation, clearSession],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
