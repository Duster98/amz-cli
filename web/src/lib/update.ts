import type { Translate } from '../i18n/types'

export function updateStageLabel(
  stage: string | null | undefined,
  t: Translate,
): string | null {
  switch (stage) {
    case 'download':
      return t('status.stageDownload')
    case 'extract':
      return t('status.stageExtract')
    case 'backup':
      return t('status.stageBackup')
    case 'preflight':
      return t('status.stagePreflight')
    case 'apply':
      return t('status.stageApply')
    case 'verify':
      return t('status.stageVerify')
    case 'completed':
      return t('status.stageCompleted')
    case 'recovery':
      return t('status.stageRecovery')
    default:
      return null
  }
}
