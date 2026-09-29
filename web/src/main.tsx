import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './auth/AuthContext'
import { ApplicationsPage } from './pages/ApplicationsPage'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { PrepPage } from './pages/PrepPage'
import { ProfilePage } from './pages/ProfilePage'
import { SignupPage } from './pages/SignupPage'
import { SupportPage } from './pages/SupportPage'
import { TailoringPage } from './pages/TailoringPage'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Retry transient failures, but never an auth failure: a 401 means the
      // session is gone, and retrying it just produces a second identical 401.
      retry: (failureCount, error) => {
        const status = (error as { status?: number } | null)?.status
        if (status === 401 || status === 403) return false
        return failureCount < 1
      },
      refetchOnWindowFocus: false,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <Routes>
            <Route path="/" element={<Navigate to="/login" replace />} />
            <Route path="/login" element={<LoginPage />} />
            <Route path="/signup" element={<SignupPage />} />
            <Route path="/app" element={<DashboardPage />} />
            <Route path="/tailoring" element={<TailoringPage />} />
            <Route path="/prep" element={<PrepPage />} />
            <Route path="/applications" element={<ApplicationsPage />} />
            <Route path="/profile" element={<ProfilePage />} />
            <Route path="/help" element={<SupportPage />} />
            <Route path="*" element={<Navigate to="/login" replace />} />
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
