import { useState, useEffect } from 'react'
import reactLogo from './assets/react.svg'
import viteLogo from '/vite.svg'
import './App.css'

interface WeatherForecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string;
}

function App() {
  const [count, setCount] = useState(0)
  const [weather, setWeather] = useState<WeatherForecast[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchWeather = async () => {
    setLoading(true)
    setError(null)
    try {
      const response = await fetch('/api/weatherforecast')
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`)
      }
      const data = await response.json()
      setWeather(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch weather data')
      console.error('Error fetching weather:', err)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchWeather()
  }, [])

  return (
    <>
      <div>
        <a href="https://vite.dev" target="_blank">
          <img src={viteLogo} className="logo" alt="Vite logo" />
        </a>
        <a href="https://react.dev" target="_blank">
          <img src={reactLogo} className="logo react" alt="React logo" />
        </a>
      </div>
      <h1>Cantaro - Music Playlist Manager</h1>
      
      <div className="card">
        <h2>Frontend ↔️ API Communication Demo</h2>
        <button onClick={fetchWeather} disabled={loading}>
          {loading ? 'Loading...' : 'Refresh Weather'}
        </button>
        
        {error && (
          <div style={{ color: 'red', margin: '1rem 0' }}>
            Error: {error}
          </div>
        )}
        
        {weather.length > 0 && (
          <div style={{ marginTop: '1rem', textAlign: 'left' }}>
            <h3>Weather Forecast (from API):</h3>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  <th style={{ border: '1px solid #ccc', padding: '0.5rem' }}>Date</th>
                  <th style={{ border: '1px solid #ccc', padding: '0.5rem' }}>Temp (°C)</th>
                  <th style={{ border: '1px solid #ccc', padding: '0.5rem' }}>Temp (°F)</th>
                  <th style={{ border: '1px solid #ccc', padding: '0.5rem' }}>Summary</th>
                </tr>
              </thead>
              <tbody>
                {weather.map((forecast, index) => (
                  <tr key={index}>
                    <td style={{ border: '1px solid #ccc', padding: '0.5rem' }}>{forecast.date}</td>
                    <td style={{ border: '1px solid #ccc', padding: '0.5rem' }}>{forecast.temperatureC}</td>
                    <td style={{ border: '1px solid #ccc', padding: '0.5rem' }}>{forecast.temperatureF}</td>
                    <td style={{ border: '1px solid #ccc', padding: '0.5rem' }}>{forecast.summary}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card">
        <button onClick={() => setCount((count) => count + 1)}>
          count is {count}
        </button>
        <p>
          Edit <code>src/App.tsx</code> and save to test HMR
        </p>
      </div>
      
      <p className="read-the-docs">
        Click on the Vite and React logos to learn more
      </p>
    </>
  )
}

export default App
