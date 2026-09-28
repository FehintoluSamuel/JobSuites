import { createContext, useCallback, useContext, useMemo, type ReactNode } from 'react'
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
          return null
        }
        throw err
      }
    },
    enabled: Boolean(tokenStore.get()),
    staleTime: 5 * 60_000,
    retry: false,
  })

  const clearSession = useCallback(() => {
    // Cancel anything still in flight, then drop the cache and the token so no
    // authenticated query can refire after sign-out.
    void queryClient.cancelQueries()
    tokenStore.clear()
    queryClient.clear()
  }, [queryClient])

  const applyAuth = useCallback(
    (result: { token: string; user: User }) => {
      tokenStore.set(result.token)
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
