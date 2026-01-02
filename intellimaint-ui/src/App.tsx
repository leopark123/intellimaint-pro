import { BrowserRouter } from 'react-router-dom'
import { AuthProvider } from './store/authStore'
import AppRouter from './router'

function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <AppRouter />
      </BrowserRouter>
    </AuthProvider>
  )
}

export default App
