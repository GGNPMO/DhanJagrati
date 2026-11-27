import { Navigate, useParams } from 'react-router'
import { masterConfigs } from './configs'
import { MasterPage } from './MasterPage'

export function MasterRoute() {
  const { entity = '' } = useParams()
  const config = masterConfigs[entity]
  if (!config) return <Navigate to="/" replace />
  // key: reset all page state when switching between masters
  return <MasterPage key={entity} config={config} />
}
