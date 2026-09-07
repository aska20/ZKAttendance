import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.jsx'
import { AuthProvider } from './context/AuthContext.jsx'
import { CalendarProvider } from './context/CalendarContext.jsx'
import { FeedbackProvider } from './components/feedback.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter>
      <FeedbackProvider>
        <AuthProvider>
          <CalendarProvider>
            <App />
          </CalendarProvider>
        </AuthProvider>
      </FeedbackProvider>
    </BrowserRouter>
  </StrictMode>,
)
