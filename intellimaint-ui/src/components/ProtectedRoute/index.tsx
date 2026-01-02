import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../../store/authStore'

interface ProtectedRouteProps {
  children: React.ReactNode
}

export default function ProtectedRoute({ children }: ProtectedRouteProps) {
  const { auth } = useAuth()
  const location = useLocation()

  if (!auth.isAuthenticated) {
    // 重定向到登录页，并保存当前路径
    return <Navigate to="/login" state={{ from: location.pathname }} replace />
  }

  return <>{children}</>
}
