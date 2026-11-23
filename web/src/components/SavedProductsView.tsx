import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { createPortal } from 'react-dom'
import type {
  Library,
  LibraryGroup,
  LibraryProduct,
  ProductResponse,
} from '../lib/api'
import { formatPrice, parseRating } from '../lib/format'
import { useI18n } from '../i18n/I18nProvider'
import {
  groupAccentClass,
  groupAccentHexValue,
  groupAccentPalette,
  groupAccentStyle,
  groupMinPrice,
  groupPriceTotals,
  groupProducts,
  isSavedProductStale,
  priceDelta,
  productGroupIds,
  savedProductCheckedAt,
  wasSavedProductCheckedWithin,
  type GroupPriceTotal,
} from '../lib/library'
import type { ResultsLayout } from './ResultsList'
import { Icon, ProductImage, RatingStars, Tag } from './ui'

type SavedTab = 'groups' | 'products'
type GroupDialogState =
  | { mode: 'create'; productIds: string[] }
  | { mode: 'rename'; group: LibraryGroup }
  | null

type GroupRefreshPlan = {
  group: LibraryGroup
  pending: LibraryProduct[]
  skipped: number
}

type GroupRefreshState = {
  active: boolean
  total: number
  done: number
  failed: number
  skipped: number
  current: string | null
}

function abortableDelay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      signal.removeEventListener('abort', abort)
      resolve()
    }, ms)
    const abort = () => {
      window.clearTimeout(timer)
      reject(new DOMException('Aborted', 'AbortError'))
    }
    if (signal.aborted) {
      abort()
      return
    }
    signal.addEventListener('abort', abort, { once: true })
  })
}

export function SavedProductsView({
  library,
  layout,
  tab,
  activeGroupId,
  error,
  savedBadge,
  onTabChange,
  onLayoutChange,
  onOpenProduct,
  onOpenGroup,
  onCloseGroup,
  onCreateGroup,
  onRenameGroup,
  onDeleteGroup,
  onAddProductsToGroup,
  onRemoveGroupProduct,
  onRemoveProduct,
  onRefreshSavedProduct,
  groupRefreshCooldownMinutes,
  groupRefreshDelaySeconds,
  autoRefreshAfterMinutes,
}: {
  library: Library
  layout: ResultsLayout
  tab: SavedTab
  activeGroupId: string | null
  error: string | null
  savedBadge: boolean
  onTabChange: (tab: SavedTab) => void
  onLayoutChange: (layout: ResultsLayout) => void
  onOpenProduct: (asin: string, domain: string) => void
  onOpenGroup: (groupId: string) => void
  onCloseGroup: () => void
  onCreateGroup: (
    name: string,
    accent: LibraryGroup['accent'],
    description: string,
    productIds: string[],
  ) => void
  onRenameGroup: (
    groupId: string,
    name: string,
    accent: LibraryGroup['accent'],
    description: string,
  ) => void
  onDeleteGroup: (groupId: string) => void
  onAddProductsToGroup: (groupId: string, productIds: string[]) => void
  onRemoveGroupProduct: (groupId: string, productId: string) => void
  onRemoveProduct: (productId: string) => void
  onRefreshSavedProduct: (
    asin: string,
    domain: string,
    signal: AbortSignal,
  ) => Promise<ProductResponse>
  groupRefreshCooldownMinutes: number
  groupRefreshDelaySeconds: number
  autoRefreshAfterMinutes: number
}) {
  const { t, formatNumber } = useI18n()
  const [groupDialog, setGroupDialog] = useState<GroupDialogState>(null)
  const [addProductsGroupId, setAddProductsGroupId] = useState<string | null>(
    null,
  )
  const [pickerProductId, setPickerProductId] = useState<string | null>(null)
  const [deleteGroupId, setDeleteGroupId] = useState<string | null>(null)
  const [searchOpen, setSearchOpen] = useState(false)
  const [searchQuery, setSearchQuery] = useState('')
  const searchInputRef = useRef<HTMLInputElement>(null)
  const [refreshPlan, setRefreshPlan] = useState<GroupRefreshPlan | null>(
    null,
  )
  const [groupRefresh, setGroupRefresh] =
    useState<GroupRefreshState | null>(null)
  const [refreshNotice, setRefreshNotice] = useState<string | null>(null)
  const [refreshNoticeTone, setRefreshNoticeTone] = useState<
    'success' | 'warning'
  >('success')
  const groupRefreshAbortRef = useRef<AbortController | null>(null)

  const activeGroup =
    activeGroupId === null
      ? null
      : library.groups.find((group) => group.id === activeGroupId) ?? null
  const searchTerm = searchQuery.trim().toLowerCase()
  const visibleProducts = useMemo(() => {
    if (!searchTerm) {
      return library.products
    }
    return library.products.filter((product) =>
      productMatches(product, searchTerm),
    )
  }, [library.products, searchTerm])
  const visibleGroups = useMemo(() => {
    if (!searchTerm) {
      return library.groups
    }
    return library.groups.filter((group) =>
      groupMatches(group, library.products, searchTerm),
    )
  }, [library.groups, library.products, searchTerm])
  const hasSearch = searchTerm.length > 0
  const searchCount =
    tab === 'groups' ? visibleGroups.length : visibleProducts.length

  useEffect(() => {
    if (!searchOpen) {
      return
    }
    searchInputRef.current?.focus()
  }, [searchOpen])

  useEffect(
    () => () => {
      groupRefreshAbortRef.current?.abort()
    },
    [],
  )

  useEffect(() => {
    groupRefreshAbortRef.current?.abort()
    setGroupRefresh(null)
    setRefreshPlan(null)
    setRefreshNotice(null)
  }, [activeGroupId])

  const planGroupRefresh = (group: LibraryGroup) => {
    const products = groupProducts(group, library.products)
    const pending = products.filter(
      (product) =>
        !wasSavedProductCheckedWithin(
          product,
          groupRefreshCooldownMinutes,
        ),
    )
    const skipped = products.length - pending.length
    setRefreshNotice(null)
    if (pending.length === 0) {
      setRefreshNoticeTone('success')
      setRefreshNotice(
        t('saved.refreshGroupAllFresh', {
          count: formatNumber(products.length),
        }),
      )
      return
    }
    setRefreshPlan({ group, pending, skipped })
  }

  const startGroupRefresh = async (plan: GroupRefreshPlan) => {
    setRefreshPlan(null)
    setRefreshNotice(null)
    groupRefreshAbortRef.current?.abort()
    const controller = new AbortController()
    groupRefreshAbortRef.current = controller
    let done = 0
    let failed = 0
    setGroupRefresh({
      active: true,
      total: plan.pending.length,
      done,
      failed,
      skipped: plan.skipped,
      current: null,
    })

    try {
      for (const [index, product] of plan.pending.entries()) {
        if (controller.signal.aborted) {
          break
        }
        setGroupRefresh({
          active: true,
          total: plan.pending.length,
          done,
          failed,
          skipped: plan.skipped,
          current: product.snapshot.title,
        })
        try {
          const refreshed = await onRefreshSavedProduct(
            product.asin,
            product.domain,
            controller.signal,
          )
          if (refreshed.stale) {
            failed += 1
          } else {
            done += 1
          }
        } catch {
          if (controller.signal.aborted) {
            break
          }
          failed += 1
        }
        setGroupRefresh({
          active: true,
          total: plan.pending.length,
          done,
          failed,
          skipped: plan.skipped,
          current: product.snapshot.title,
        })
        if (index < plan.pending.length - 1) {
          await abortableDelay(
            groupRefreshDelaySeconds * 1000,
            controller.signal,
          )
        }
      }
    } catch (caught) {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        failed += 1
      }
    } finally {
      if (groupRefreshAbortRef.current === controller) {
        groupRefreshAbortRef.current = null
      }
      setGroupRefresh(null)
      if (controller.signal.aborted) {
        setRefreshNoticeTone('warning')
        setRefreshNotice(t('saved.refreshGroupCancelled'))
      } else {
        setRefreshNoticeTone(failed > 0 ? 'warning' : 'success')
        setRefreshNotice(
          t('saved.refreshGroupDone', {
            updated: formatNumber(done),
            failed: formatNumber(failed),
            skipped: formatNumber(plan.skipped),
          }),
        )
      }
    }
  }

  const cancelGroupRefresh = () => {
    groupRefreshAbortRef.current?.abort()
  }

  const addProductsGroup =
    addProductsGroupId === null
      ? null
      : library.groups.find((group) => group.id === addProductsGroupId) ?? null
  const pickerProduct =
    pickerProductId === null
      ? null
      : library.products.find((product) => product.id === pickerProductId) ??
        null
  const deleteGroup =
    deleteGroupId === null
      ? null
      : library.groups.find((group) => group.id === deleteGroupId) ?? null

  if (activeGroup) {
    const items = groupProducts(activeGroup, library.products)
    return (
      <section className="saved-view">
        <GroupHeader
          group={activeGroup}
          products={library.products}
          refreshState={groupRefresh}
          autoRefreshAfterMinutes={autoRefreshAfterMinutes}
          onBack={onCloseGroup}
          onRefreshPrices={() => planGroupRefresh(activeGroup)}
          onCancelRefresh={cancelGroupRefresh}
          onRename={() =>
            setGroupDialog({ mode: 'rename', group: activeGroup })
          }
          onDelete={() => setDeleteGroupId(activeGroup.id)}
          onAddProducts={() => setAddProductsGroupId(activeGroup.id)}
        />
        {groupRefresh?.active ? (
          <div
            className="progress-block saved-group-refresh-progress"
            role="status"
            aria-live="polite"
          >
            <div className="progress-line">
              <span
                style={{
                  width: `${
                    groupRefresh.total === 0
                      ? 0
                      : (groupRefresh.done / groupRefresh.total) * 100
                  }%`,
                }}
              />
            </div>
            <div className="progress-copy">
              <span>
                {t('saved.refreshGroupProgress', {
                  done: formatNumber(groupRefresh.done),
                  total: formatNumber(groupRefresh.total),
                  title: groupRefresh.current ?? '',
                })}
              </span>
              <button
                className="text-button"
                type="button"
                onClick={cancelGroupRefresh}
              >
                {t('saved.cancelRefresh')}
              </button>
            </div>
          </div>
        ) : null}
        {refreshNotice ? (
          <div
            className={`notice ${
              refreshNoticeTone === 'success'
                ? 'notice-success'
                : 'notice-warning'
            }`}
            role="status"
          >
            <Icon name={refreshNoticeTone === 'success' ? 'check' : 'info'} />
            <div>
              <p>{refreshNotice}</p>
              <button
                className="text-button"
                type="button"
                onClick={() => setRefreshNotice(null)}
              >
                {t('saved.dismiss')}
              </button>
            </div>
          </div>
        ) : null}
        {items.length === 0 ? (
          <EmptyState
            icon="folder"
            title={t('saved.emptyGroupTitle')}
            body={t('saved.emptyGroupBody')}
          />
        ) : (
          <SavedProductLayout
            products={items}
            groups={library.groups}
            layout={layout}
            excludeGroupId={activeGroup.id}
            removeMode="group"
            onOpenProduct={onOpenProduct}
            onRemoveProduct={(productId) =>
              onRemoveGroupProduct(activeGroup.id, productId)
            }
            onAddToGroup={null}
            autoRefreshAfterMinutes={autoRefreshAfterMinutes}
          />
        )}
        {groupDialog ? (
          <GroupEditorDialog
            mode="rename"
            group={groupDialog.mode === 'rename' ? groupDialog.group : null}
            products={library.products}
            initialProductIds={[]}
            onClose={() => setGroupDialog(null)}
            onSubmit={(name, accent, description) => {
              if (groupDialog.mode === 'rename') {
                onRenameGroup(
                  groupDialog.group.id,
                  name,
                  accent,
                  description,
                )
              }
              setGroupDialog(null)
            }}
          />
        ) : null}
        {addProductsGroup ? (
          <AddProductsDialog
            group={addProductsGroup}
            products={library.products}
            onClose={() => setAddProductsGroupId(null)}
            onSubmit={(productIds) => {
              onAddProductsToGroup(addProductsGroup.id, productIds)
              setAddProductsGroupId(null)
            }}
          />
        ) : null}
        {refreshPlan ? (
          <ConfirmDialog
            title={t('saved.refreshGroupTitle')}
            body={t('saved.refreshGroupBody', {
              count: formatNumber(refreshPlan.pending.length),
              skipped: formatNumber(refreshPlan.skipped),
              minutes: formatNumber(
                Math.max(
                  1,
                  Math.ceil(
                    (refreshPlan.pending.length *
                      (groupRefreshDelaySeconds + 5)) /
                      60,
                  ),
                ),
              ),
            })}
            confirmLabel={t('saved.refreshGroupStart')}
            onConfirm={() => {
              const plan = refreshPlan
              setRefreshPlan(null)
              void startGroupRefresh(plan)
            }}
            onClose={() => setRefreshPlan(null)}
          />
        ) : null}
        {deleteGroup ? (
          <ConfirmDialog
            title={t('saved.deleteGroup')}
            body={t('saved.deleteGroupBody', { name: deleteGroup.name })}
            confirmLabel={t('saved.delete')}
            onConfirm={() => {
              onDeleteGroup(deleteGroup.id)
              setDeleteGroupId(null)
              onCloseGroup()
            }}
            onClose={() => setDeleteGroupId(null)}
          />
        ) : null}
      </section>
    )
  }

  return (
    <section className="saved-view">
      <header className="saved-header saved-products-header">
        <div className="saved-heading">
          <h1>{t('saved.title')}</h1>
        </div>
        <div className="saved-header-actions">
          <div className="saved-tabs" role="tablist" aria-label={t('saved.title')}>
            <button
              className={`saved-tab${tab === 'groups' ? ' is-active' : ''}`}
              type="button"
              role="tab"
              aria-selected={tab === 'groups'}
              onClick={() => onTabChange('groups')}
            >
              {t('saved.groupsTab')}
              <span className="saved-tab-count">
                {formatNumber(library.groups.length)}
              </span>
            </button>
            <button
              className={`saved-tab${tab === 'products' ? ' is-active' : ''}`}
              type="button"
              role="tab"
              aria-selected={tab === 'products'}
              onClick={() => onTabChange('products')}
            >
              {t('saved.allProducts')}
              <span className="saved-tab-count">
                {formatNumber(library.products.length)}
              </span>
              {savedBadge ? (
                <span className="saved-tab-dot" aria-hidden="true" />
              ) : null}
            </button>
          </div>
          <div className="saved-header-tools">
            <button
              className={`saved-search-toggle${searchOpen ? ' is-active' : ''}`}
              type="button"
              aria-expanded={searchOpen}
              aria-controls="saved-search-panel"
              aria-label={t('saved.searchSaved')}
              title={t('saved.searchSaved')}
              onClick={() => {
                if (searchOpen) {
                  setSearchOpen(false)
                  setSearchQuery('')
                } else {
                  setSearchOpen(true)
                }
              }}
            >
              <Icon name="search" />
            </button>
            <div className="results-layout" role="group" aria-label={t('saved.layout')}>
              <button
                type="button"
                className={layout === 'list' ? 'is-active' : ''}
                aria-pressed={layout === 'list'}
                title={t('saved.listView')}
                onClick={() => onLayoutChange('list')}
              >
                <Icon name="list" />
              </button>
              <button
                type="button"
                className={layout === 'grid' ? 'is-active' : ''}
                aria-pressed={layout === 'grid'}
                title={t('saved.gridView')}
                onClick={() => onLayoutChange('grid')}
              >
                <Icon name="grid" />
              </button>
            </div>
          </div>
        </div>
      </header>

      {searchOpen ? (
        <div className="saved-search-panel" id="saved-search-panel">
          <Icon name="search" />
          <input
            ref={searchInputRef}
            type="search"
            value={searchQuery}
            placeholder={t('saved.searchPlaceholder')}
            aria-label={t('saved.searchSaved')}
            onChange={(event) => setSearchQuery(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Escape') {
                setSearchOpen(false)
                setSearchQuery('')
              }
            }}
          />
          {hasSearch ? (
            <span className="saved-search-count">
              {t(
                searchCount === 1
                  ? 'saved.searchCountOne'
                  : 'saved.searchCount',
                { count: formatNumber(searchCount) },
              )}
            </span>
          ) : null}
          {hasSearch ? (
            <button
              className="saved-search-clear"
              type="button"
              aria-label={t('saved.clearSearch')}
              title={t('saved.clearSearch')}
              onClick={() => {
                setSearchQuery('')
                searchInputRef.current?.focus()
              }}
            >
              <Icon name="close" />
            </button>
          ) : null}
        </div>
      ) : null}

      {error ? (
        <div className="notice notice-warning" role="alert">
          <Icon name="alert" />
          <div>
            <strong>{t('saved.saveFailed')}</strong>
            <p>{error}</p>
          </div>
        </div>
      ) : null}

      {tab === 'groups' ? (
        visibleGroups.length === 0 ? (
          <EmptyState
            icon="folder"
            title={
              hasSearch
                ? t('saved.searchNoResultsTitle')
                : t('saved.emptyGroupsTitle')
            }
            body={
              hasSearch
                ? t('saved.searchNoResultsBody')
                : t('saved.emptyGroupsBody')
            }
          />
        ) : (
          <div
            className={`saved-groups saved-groups--${layout}`}
            role="list"
            aria-label={t('saved.groupsTab')}
          >
            {visibleGroups.map((group) => (
              <SavedGroupCard
                key={group.id}
                group={group}
                products={library.products}
                layout={layout}
                onOpen={() => onOpenGroup(group.id)}
                onRename={() =>
                  setGroupDialog({ mode: 'rename', group })
                }
                onDelete={() => setDeleteGroupId(group.id)}
              />
            ))}
          </div>
        )
      ) : (
        <>
          {visibleProducts.length === 0 ? (
            <EmptyState
              icon="bookmark"
              title={
                hasSearch
                  ? t('saved.searchNoResultsTitle')
                  : t('saved.emptyProductsTitle')
              }
              body={
                hasSearch
                  ? t('saved.searchNoResultsBody')
                  : t('saved.emptyProductsBody')
              }
            />
          ) : (
            <SavedProductLayout
              products={visibleProducts}
              groups={library.groups}
              layout={layout}
              removeMode="saved"
              onOpenProduct={onOpenProduct}
              onRemoveProduct={onRemoveProduct}
              onAddToGroup={(productId) => setPickerProductId(productId)}
              autoRefreshAfterMinutes={autoRefreshAfterMinutes}
            />
          )}
        </>
      )}

      {groupDialog ? (
        <GroupEditorDialog
          mode={groupDialog.mode}
          group={groupDialog.mode === 'rename' ? groupDialog.group : null}
          products={library.products}
          initialProductIds={
            groupDialog.mode === 'create' ? groupDialog.productIds : []
          }
          onClose={() => setGroupDialog(null)}
          onSubmit={(name, accent, description, productIds) => {
            if (groupDialog.mode === 'rename') {
              onRenameGroup(
                groupDialog.group.id,
                name,
                accent,
                description,
              )
            } else {
              onCreateGroup(name, accent, description, productIds)
            }
            setGroupDialog(null)
          }}
        />
      ) : null}

      {addProductsGroup ? (
        <AddProductsDialog
          group={addProductsGroup}
          products={library.products}
          onClose={() => setAddProductsGroupId(null)}
          onSubmit={(productIds) => {
            onAddProductsToGroup(addProductsGroup.id, productIds)
            setAddProductsGroupId(null)
          }}
        />
      ) : null}

      {pickerProduct ? (
        <GroupPickerDialog
          product={pickerProduct}
          groups={library.groups}
          onClose={() => setPickerProductId(null)}
          onPick={(groupId) => {
            onAddProductsToGroup(groupId, [pickerProduct.id])
            setPickerProductId(null)
          }}
          onCreate={() => {
            setGroupDialog({
              mode: 'create',
              productIds: [pickerProduct.id],
            })
            setPickerProductId(null)
          }}
        />
      ) : null}

      {deleteGroup ? (
        <ConfirmDialog
          title={t('saved.deleteGroup')}
          body={t('saved.deleteGroupBody', { name: deleteGroup.name })}
          confirmLabel={t('saved.delete')}
          onConfirm={() => {
            onDeleteGroup(deleteGroup.id)
            setDeleteGroupId(null)
          }}
          onClose={() => setDeleteGroupId(null)}
        />
      ) : null}

      {createPortal(
        <button
          className="saved-fab"
          type="button"
          aria-label={t('saved.newGroup')}
          title={t('saved.newGroup')}
          onClick={() => setGroupDialog({ mode: 'create', productIds: [] })}
        >
          <Icon name="plus" />
        </button>,
        document.body,
      )}
    </section>
  )
}

function GroupHeader({
  group,
  products,
  refreshState,
  autoRefreshAfterMinutes,
  onBack,
  onRefreshPrices,
  onCancelRefresh,
  onRename,
  onDelete,
  onAddProducts,
}: {
  group: LibraryGroup
  products: LibraryProduct[]
  refreshState: GroupRefreshState | null
  autoRefreshAfterMinutes: number
  onBack: () => void
  onRefreshPrices: () => void
  onCancelRefresh: () => void
  onRename: () => void
  onDelete: () => void
  onAddProducts: () => void
}) {
  const { t, formatNumber, languageLocale } = useI18n()
  const items = groupProducts(group, products)
  const staleCount = items.filter((product) =>
    isSavedProductStale(product, autoRefreshAfterMinutes),
  ).length
  const totals = groupPriceTotals(group, products)
  const minPrice = groupMinPrice(group, products)
  const priced = totals.reduce((sum, total) => sum + total.priced, 0)
  const totalLabel = formatGroupTotals(
    totals,
    languageLocale,
    t('saved.groupNoPrice'),
  )
  const minLabel =
    minPrice !== null
      ? formatPrice(minPrice.amount, minPrice.currency, '', languageLocale)
      : t('saved.groupNoPrice')
  const partial =
    priced > 0 && priced < items.length
      ? t('saved.groupTotalPartial', {
          priced: formatNumber(priced),
          count: formatNumber(items.length),
        })
      : null

  return (
    <header
      className={`saved-group-header ${groupAccentClass(group.accent)}`}
      style={groupAccentStyle(group.accent)}
    >
      <div className="saved-group-nav">
        <button className="back-button" type="button" onClick={onBack}>
          <Icon name="arrow-left" />
          {t('saved.backToGroups')}
        </button>
        <div className="saved-group-actions">
          <button
            className="saved-group-add"
            type="button"
            onClick={onAddProducts}
          >
            <Icon name="plus" />
            {t('saved.addProducts')}
          </button>
          <button
            className="saved-group-refresh"
            type="button"
            disabled={items.length === 0 || refreshState?.active}
            onClick={onRefreshPrices}
          >
            <Icon name="refresh" />
            {refreshState?.active
              ? t('saved.refreshingGroup', {
                  done: formatNumber(refreshState.done),
                  total: formatNumber(refreshState.total),
                })
              : t('saved.refreshGroup')}
          </button>
          {refreshState?.active ? (
            <button
              className="text-button"
              type="button"
              onClick={onCancelRefresh}
            >
              {t('saved.cancelRefresh')}
            </button>
          ) : null}
          <button className="text-button" type="button" onClick={onRename}>
            {t('saved.editGroup')}
          </button>
          <button className="text-button danger" type="button" onClick={onDelete}>
            {t('saved.deleteGroup')}
          </button>
        </div>
      </div>
      <div className="saved-group-hero">
        <div className="saved-group-hero-copy">
          <p className="saved-group-kicker">
            <span className="saved-group-kicker-mark" aria-hidden="true" />
            {t('saved.groupLabel')}
          </p>
          <h1>{group.name}</h1>
          {group.description ? (
            <p className="saved-group-description">{group.description}</p>
          ) : null}
          {staleCount > 0 ? (
            <p className="saved-group-stale-note">
              {t('saved.groupStaleNote', {
                count: formatNumber(staleCount),
              })}
            </p>
          ) : null}
          <dl className="saved-group-facts">
            <div className="saved-group-fact">
              <dt>{t('saved.productsLabel')}</dt>
              <dd>{formatNumber(items.length)}</dd>
            </div>
            {items.length > 0 ? (
              <div className="saved-group-fact saved-group-fact--total">
                <dt>{t('saved.total')}</dt>
                <dd>
                  {totalLabel}
                  {partial ? (
                    <span className="saved-group-fact-note">{partial}</span>
                  ) : null}
                </dd>
              </div>
            ) : null}
            {minPrice !== null ? (
              <div className="saved-group-fact">
                <dt>{t('saved.fromLabel')}</dt>
                <dd>{minLabel}</dd>
              </div>
            ) : null}
          </dl>
        </div>
        <GroupCover items={items} variant="hero" />
      </div>
    </header>
  )
}

function SavedGroupCard({
  group,
  products,
  layout,
  onOpen,
  onRename,
  onDelete,
}: {
  group: LibraryGroup
  products: LibraryProduct[]
  layout: ResultsLayout
  onOpen: () => void
  onRename: () => void
  onDelete: () => void
}) {
  const { t, formatNumber, languageLocale } = useI18n()
  const items = groupProducts(group, products)
  const totals = groupPriceTotals(group, products)
  const priced = totals.reduce((sum, total) => sum + total.priced, 0)
  const totalLabel = formatGroupTotals(
    totals,
    languageLocale,
    t('saved.groupNoPrice'),
  )
  const countLabel =
    items.length === 1
      ? t('saved.groupCountOne', { count: formatNumber(items.length) })
      : t('saved.groupCount', { count: formatNumber(items.length) })
  const partial =
    priced > 0 && priced < items.length
      ? t('saved.groupTotalPartial', {
          priced: formatNumber(priced),
          count: formatNumber(items.length),
        })
      : null

  return (
    <article
      className={`saved-group-card saved-group-card--${layout} ${groupAccentClass(group.accent)}`}
      style={groupAccentStyle(group.accent)}
      role="listitem"
    >
      <button className="saved-group-open" type="button" onClick={onOpen}>
        <GroupCover items={items} />
        <span className="saved-group-copy">
          <span className="saved-group-name-row">
            <span className="saved-group-name">{group.name}</span>
            <span className="saved-group-open-arrow" aria-hidden="true">
              <Icon name="arrow-right" />
            </span>
          </span>
          {group.description ? (
            <span className="saved-group-description">
              {group.description}
            </span>
          ) : null}
          <span className="saved-group-footer">
            <span className="saved-group-count">{countLabel}</span>
            {items.length > 0 ? (
              <span className="saved-group-price">
                <span className="saved-group-price-label">
                  {t('saved.total')}
                </span>
                <span
                  className="saved-group-price-value"
                  title={partial ?? undefined}
                >
                  {totalLabel}
                </span>
              </span>
            ) : null}
          </span>
        </span>
      </button>
      <GroupMenu onRename={onRename} onDelete={onDelete} />
    </article>
  )
}

function GroupCover({
  items,
  variant = 'card',
}: {
  items: LibraryProduct[]
  variant?: 'card' | 'hero'
}) {
  const { formatNumber } = useI18n()
  const preview = items.slice(0, 5)

  return (
    <span
      className={`saved-group-cover saved-group-cover--${variant}`}
      data-count={preview.length}
      aria-hidden="true"
    >
      {preview.length === 0 ? (
        <span className="saved-group-cover-empty">
          <Icon name="folder" />
        </span>
      ) : (
        preview.map((item, index) => (
          <span
            className={`saved-group-tile saved-group-tile--${index + 1}`}
            key={item.id}
          >
            <ProductImage src={item.snapshot.image} alt={item.snapshot.title} />
          </span>
        ))
      )}
      {items.length > preview.length ? (
        <span className="saved-group-cover-more">
          +{formatNumber(items.length - preview.length)}
        </span>
      ) : null}
    </span>
  )
}

function GroupMenu({
  onRename,
  onDelete,
}: {
  onRename: () => void
  onDelete: () => void
}) {
  const { t } = useI18n()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
        triggerRef.current?.focus()
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  const run = (action: () => void) => {
    setOpen(false)
    action()
  }

  return (
    <div className="saved-group-menu" ref={rootRef}>
      <button
        ref={triggerRef}
        className="saved-group-menu-trigger"
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={t('saved.groupActions')}
        title={t('saved.groupActions')}
        onClick={() => setOpen((value) => !value)}
      >
        <Icon name="more" />
      </button>
      {open ? (
        <div className="saved-group-menu-list" role="menu">
          <button
            type="button"
            role="menuitem"
            autoFocus
            onClick={() => run(onRename)}
          >
            {t('saved.editGroup')}
          </button>
          <button
            className="danger"
            type="button"
            role="menuitem"
            onClick={() => run(onDelete)}
          >
            {t('saved.deleteGroup')}
          </button>
        </div>
      ) : null}
    </div>
  )
}

function formatGroupTotals(
  totals: GroupPriceTotal[],
  locale: string,
  fallback: string,
): string {
  if (totals.length === 0) {
    return fallback
  }
  return totals
    .map((total) => formatPrice(total.amount, total.currency, '', locale))
    .join(' + ')
}

function productMatches(product: LibraryProduct, query: string): boolean {
  return (
    product.snapshot.title.toLowerCase().includes(query) ||
    product.asin.toLowerCase().includes(query) ||
    product.domain.toLowerCase().includes(query)
  )
}

function groupMatches(
  group: LibraryGroup,
  products: LibraryProduct[],
  query: string,
): boolean {
  if (
    group.name.toLowerCase().includes(query) ||
    (group.description ?? '').toLowerCase().includes(query)
  ) {
    return true
  }
  return groupProducts(group, products).some((product) =>
    productMatches(product, query),
  )
}

function SavedProductLayout({
  products,
  groups,
  layout,
  excludeGroupId,
  removeMode,
  onOpenProduct,
  onRemoveProduct,
  onAddToGroup,
  autoRefreshAfterMinutes,
}: {
  products: LibraryProduct[]
  groups: LibraryGroup[]
  layout: ResultsLayout
  excludeGroupId?: string
  removeMode: 'saved' | 'group'
  onOpenProduct: (asin: string, domain: string) => void
  onRemoveProduct: (productId: string) => void
  onAddToGroup: ((productId: string) => void) | null
  autoRefreshAfterMinutes: number
}) {
  if (layout === 'grid') {
    return (
      <ul className="saved-products saved-products--grid">
        {products.map((product) => (
          <SavedProductCard
            key={product.id}
            product={product}
            groups={groups}
            layout="grid"
            excludeGroupId={excludeGroupId}
            removeMode={removeMode}
            onOpen={() => onOpenProduct(product.asin, product.domain)}
            onRemove={() => onRemoveProduct(product.id)}
            onAddToGroup={
              onAddToGroup ? () => onAddToGroup(product.id) : null
            }
            autoRefreshAfterMinutes={autoRefreshAfterMinutes}
          />
        ))}
      </ul>
    )
  }

  return (
    <ol className="saved-products saved-products--list">
      {products.map((product) => (
        <SavedProductCard
          key={product.id}
          product={product}
          groups={groups}
          layout="list"
          excludeGroupId={excludeGroupId}
          removeMode={removeMode}
          onOpen={() => onOpenProduct(product.asin, product.domain)}
          onRemove={() => onRemoveProduct(product.id)}
          onAddToGroup={
            onAddToGroup ? () => onAddToGroup(product.id) : null
          }
          autoRefreshAfterMinutes={autoRefreshAfterMinutes}
        />
      ))}
    </ol>
  )
}

function SavedProductCard({
  product,
  groups,
  layout,
  excludeGroupId,
  removeMode,
  onOpen,
  onRemove,
  onAddToGroup,
  autoRefreshAfterMinutes,
}: {
  product: LibraryProduct
  groups: LibraryGroup[]
  layout: ResultsLayout
  excludeGroupId?: string
  removeMode: 'saved' | 'group'
  onOpen: () => void
  onRemove: () => void
  onAddToGroup: (() => void) | null
  autoRefreshAfterMinutes: number
}) {
  const { t, languageLocale } = useI18n()
  const snapshot = product.snapshot
  const delta = priceDelta(product)
  const groupIds = productGroupIds(groups, product.id)
  const groupNames = groupIds
    .filter((id) => id !== excludeGroupId)
    .map((id) => groups.find((group) => group.id === id)?.name)
    .filter((name): name is string => Boolean(name))
  const price = formatPrice(
    product.price_last ?? snapshot.price_amount,
    product.currency || snapshot.currency,
    snapshot.price,
    languageLocale,
  )
  const savedDate = new Date(product.saved_at).toLocaleDateString(
    languageLocale,
    { day: 'numeric', month: 'short' },
  )
  const checkedDate = new Date(savedProductCheckedAt(product))
    .toLocaleDateString(languageLocale, {
      day: 'numeric',
      month: 'short',
    })
  const hasChecked = Boolean(product.last_checked_at)
  const checkedLabel = t(
    hasChecked ? 'saved.checkedOn' : 'saved.savedOn',
    { date: hasChecked ? checkedDate : savedDate },
  )
  const stale = isSavedProductStale(product, autoRefreshAfterMinutes)
  const chargerWarning =
    snapshot.charger?.included === false
      ? snapshot.charger.label || t('product.chargerNotIncluded')
      : null

  const tags = (
    <span className="result-tags">
      {groupNames.map((name) => (
        <Tag tone="neutral" key={name}>
          {name}
        </Tag>
      ))}
      {chargerWarning ? <Tag tone="rust">{chargerWarning}</Tag> : null}
      {delta ? (
        <Tag tone={delta.direction === 'down' ? 'blue' : 'rust'}>
          {delta.direction === 'down' ? '↓' : '↑'}{' '}
          {delta.percent.toLocaleString(languageLocale, {
            maximumFractionDigits: 1,
          })}
          %
        </Tag>
      ) : null}
      {stale ? <Tag tone="gold">{t('saved.checkNeeded')}</Tag> : null}
    </span>
  )

  const actions = (
    <div className="saved-product-actions">
      <button
        className={`saved-icon-button${removeMode === 'saved' ? ' is-saved' : ''}`}
        type="button"
        onClick={onRemove}
        aria-label={
          removeMode === 'group'
            ? t('saved.removeFromGroup')
            : t('saved.removeBookmark')
        }
        title={
          removeMode === 'group'
            ? t('saved.removeFromGroup')
            : t('saved.removeBookmark')
        }
      >
        <Icon name={removeMode === 'group' ? 'close' : 'bookmark-filled'} />
      </button>
      {onAddToGroup ? (
        <button
          className="saved-icon-button"
          type="button"
          onClick={onAddToGroup}
          aria-label={t('saved.addToGroup')}
          title={t('saved.addToGroup')}
        >
          <Icon name="folder" />
        </button>
      ) : null}
    </div>
  )

  if (layout === 'grid') {
    return (
      <li className="result-grid-cell saved-product-cell">
        <button
          className="result-card saved-product-card"
          type="button"
          onClick={onOpen}
        >
          <span className="result-card-image">
            <ProductImage src={snapshot.image} alt={snapshot.title} eager />
          </span>
          <span className="result-card-body">
            {tags}
            <span className="result-card-title">{snapshot.title}</span>
            <span className="result-meta">
              <RatingStars
                value={parseRating(snapshot.rating ?? undefined)}
                count={snapshot.reviews ?? undefined}
              />
              {snapshot.delivery_text ? (
                <span className="delivery-line">
                  <Icon name="package" />
                  {snapshot.delivery_text}
                </span>
              ) : null}
            </span>
            <span className="result-card-foot">
              <span className="result-card-price">
                {snapshot.original_price ? (
                  <span className="price-original">{snapshot.original_price}</span>
                ) : null}
                <span className="price">{price}</span>
              </span>
              <span
                className="saved-product-date"
                title={t('saved.savedOn', { date: savedDate })}
              >
                {checkedLabel}
              </span>
            </span>
          </span>
        </button>
        {actions}
      </li>
    )
  }

  return (
    <li className="result-list-item saved-product-row-wrap">
      <button
        className="result-row saved-product-row"
        type="button"
        onClick={onOpen}
      >
        <span className="result-image">
          <ProductImage src={snapshot.image} alt={snapshot.title} eager />
        </span>
        <span className="result-copy">
          {tags}
          <span className="result-title">{snapshot.title}</span>
          <span className="result-meta">
            <span>
              {t('saved.savedOn', { date: savedDate })}
              {hasChecked ? ` · ${checkedLabel}` : ''}
            </span>
            {snapshot.delivery_text ? (
              <span className="delivery-line">
                <Icon name="package" />
                {snapshot.delivery_text}
              </span>
            ) : null}
          </span>
        </span>
        <span className="result-price-block">
          {snapshot.original_price ? (
            <span className="price-original">{snapshot.original_price}</span>
          ) : null}
          <span className="price">{price}</span>
        </span>
      </button>
      {actions}
    </li>
  )
}

function GroupEditorDialog({
  mode,
  group,
  products,
  initialProductIds,
  onClose,
  onSubmit,
}: {
  mode: 'create' | 'rename'
  group: LibraryGroup | null
  products: LibraryProduct[]
  initialProductIds: string[]
  onClose: () => void
  onSubmit: (
    name: string,
    accent: LibraryGroup['accent'],
    description: string,
    productIds: string[],
  ) => void
}) {
  const { t } = useI18n()
  const [name, setName] = useState(group?.name ?? '')
  const [accent, setAccent] = useState<LibraryGroup['accent']>(
    group?.accent ?? 'accent',
  )
  const [description, setDescription] = useState(group?.description ?? '')
  const [selected, setSelected] = useState<string[]>(initialProductIds)
  const [query, setQuery] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  const filtered = useMemo(() => {
    const normalized = query.trim().toLowerCase()
    if (!normalized) {
      return products
    }
    return products.filter(
      (product) =>
        product.snapshot.title.toLowerCase().includes(normalized) ||
        product.asin.toLowerCase().includes(normalized),
    )
  }, [products, query])

  return (
    <Modal
      title={mode === 'create' ? t('saved.createGroup') : t('saved.editGroup')}
      onClose={onClose}
      footer={
        <>
          <button className="text-button" type="button" onClick={onClose}>
            {t('saved.cancel')}
          </button>
          <button
            className="primary-button"
            type="button"
            disabled={name.trim().length === 0}
            onClick={() => onSubmit(name, accent, description, selected)}
          >
            {mode === 'create' ? t('saved.createGroup') : t('saved.saveChanges')}
          </button>
        </>
      }
    >
      <label className="modal-field">
        <span>{t('saved.groupName')}</span>
        <input
          ref={inputRef}
          type="text"
          value={name}
          maxLength={80}
          placeholder={t('saved.groupNamePlaceholder')}
          onChange={(event) => setName(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && name.trim().length > 0) {
              event.preventDefault()
              onSubmit(name, accent, description, selected)
            }
          }}
        />
      </label>
      <label className="modal-field">
        <span>{t('saved.groupDescription')}</span>
        <textarea
          className="modal-textarea"
          value={description}
          maxLength={500}
          rows={3}
          placeholder={t('saved.groupDescriptionPlaceholder')}
          onChange={(event) => setDescription(event.target.value)}
        />
        <small className="modal-hint">
          {t('saved.groupDescriptionCount', {
            count: description.length,
          })}
        </small>
      </label>
      <div className="modal-field">
        <span>{t('saved.groupAccent')}</span>
        <div className="accent-swatches" role="group" aria-label={t('saved.groupAccent')}>
          {groupAccentPalette.map((value) => (
            <button
              key={value}
              type="button"
              className={`accent-swatch saved-accent--${value}${accent === value ? ' is-selected' : ''}`}
              aria-pressed={accent === value}
              aria-label={value}
              title={value}
              onClick={() => setAccent(value)}
            />
          ))}
        </div>
        <label className="accent-custom">
          <span
            className="accent-custom-preview"
            style={{ background: groupAccentHexValue(accent) }}
            aria-hidden="true"
          />
          <span>{t('saved.groupCustomColor')}</span>
          <input
            type="color"
            value={groupAccentHexValue(accent)}
            onChange={(event) =>
              setAccent(event.target.value as LibraryGroup['accent'])
            }
          />
          <span className="accent-custom-value">
            {groupAccentHexValue(accent).toUpperCase()}
          </span>
        </label>
      </div>
      {mode === 'create' ? (
        <div className="modal-field">
          <span>{t('saved.selectProducts')}</span>
          <input
            className="modal-search"
            type="search"
            value={query}
            placeholder={t('saved.searchProducts')}
            onChange={(event) => setQuery(event.target.value)}
          />
          <ProductChecklist
            products={filtered}
            selected={selected}
            onToggle={(productId) =>
              setSelected((current) =>
                current.includes(productId)
                  ? current.filter((id) => id !== productId)
                  : [...current, productId],
              )
            }
            emptyLabel={t('saved.noProductsToSelect')}
          />
        </div>
      ) : null}
    </Modal>
  )
}

function AddProductsDialog({
  group,
  products,
  onClose,
  onSubmit,
}: {
  group: LibraryGroup
  products: LibraryProduct[]
  onClose: () => void
  onSubmit: (productIds: string[]) => void
}) {
  const { t } = useI18n()
  const existing = new Set(group.items.map((item) => item.product_id))
  const available = products.filter((product) => !existing.has(product.id))
  const [selected, setSelected] = useState<string[]>([])
  const [query, setQuery] = useState('')
  const filtered = useMemo(() => {
    const normalized = query.trim().toLowerCase()
    if (!normalized) {
      return available
    }
    return available.filter(
      (product) =>
        product.snapshot.title.toLowerCase().includes(normalized) ||
        product.asin.toLowerCase().includes(normalized),
    )
  }, [available, query])

  return (
    <Modal
      title={t('saved.addProducts')}
      subtitle={group.name}
      onClose={onClose}
      footer={
        <>
          <button className="text-button" type="button" onClick={onClose}>
            {t('saved.cancel')}
          </button>
          <button
            className="primary-button"
            type="button"
            disabled={selected.length === 0}
            onClick={() => onSubmit(selected)}
          >
            {t('saved.add')}
          </button>
        </>
      }
    >
      <input
        className="modal-search"
        type="search"
        value={query}
        placeholder={t('saved.searchProducts')}
        onChange={(event) => setQuery(event.target.value)}
        autoFocus
      />
      <ProductChecklist
        products={filtered}
        selected={selected}
        onToggle={(productId) =>
          setSelected((current) =>
            current.includes(productId)
              ? current.filter((id) => id !== productId)
              : [...current, productId],
          )
        }
        emptyLabel={
          available.length === 0
            ? t('saved.allProductsInGroup')
            : t('saved.noProductsToSelect')
        }
      />
    </Modal>
  )
}

function GroupPickerDialog({
  product,
  groups,
  onClose,
  onPick,
  onCreate,
}: {
  product: LibraryProduct
  groups: LibraryGroup[]
  onClose: () => void
  onPick: (groupId: string) => void
  onCreate: () => void
}) {
  const { t } = useI18n()
  return (
    <Modal
      title={t('saved.addToGroup')}
      subtitle={product.snapshot.title}
      onClose={onClose}
      footer={
        <>
          <button className="text-button" type="button" onClick={onClose}>
            {t('saved.cancel')}
          </button>
          <button className="primary-button" type="button" onClick={onCreate}>
            {t('saved.newGroup')}
          </button>
        </>
      }
    >
      {groups.length === 0 ? (
        <p className="modal-empty">{t('saved.noGroupsYet')}</p>
      ) : (
        <ul className="group-picker-list">
          {groups.map((group) => (
            <li key={group.id}>
              <button
                className={`group-picker-item ${groupAccentClass(group.accent)}`}
                style={groupAccentStyle(group.accent)}
                type="button"
                onClick={() => onPick(group.id)}
              >
                <span className="group-picker-name">{group.name}</span>
                <span className="group-picker-count">
                  {group.items.length === 1
                    ? t('saved.groupCountOne', {
                        count: group.items.length,
                      })
                    : t('saved.groupCount', {
                        count: group.items.length,
                      })}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Modal>
  )
}

export function ConfirmDialog({
  title,
  body,
  confirmLabel,
  onConfirm,
  onClose,
}: {
  title: string
  body: string
  confirmLabel: string
  onConfirm: () => void
  onClose: () => void
}) {
  const { t } = useI18n()
  return (
    <Modal
      title={title}
      onClose={onClose}
      footer={
        <>
          <button className="text-button" type="button" onClick={onClose}>
            {t('saved.cancel')}
          </button>
          <button className="primary-button danger" type="button" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </>
      }
    >
      <p className="modal-text">{body}</p>
    </Modal>
  )
}

function ProductChecklist({
  products,
  selected,
  onToggle,
  emptyLabel,
}: {
  products: LibraryProduct[]
  selected: string[]
  onToggle: (productId: string) => void
  emptyLabel: string
}) {
  if (products.length === 0) {
    return <p className="modal-empty">{emptyLabel}</p>
  }
  return (
    <ul className="product-checklist">
      {products.map((product) => (
        <li key={product.id}>
          <label className="product-check-row">
            <input
              type="checkbox"
              checked={selected.includes(product.id)}
              onChange={() => onToggle(product.id)}
            />
            <ProductImage src={product.snapshot.image} alt="" />
            <span className="product-check-copy">
              <span>{product.snapshot.title}</span>
              <span className="product-check-meta">
                {product.asin} · {product.snapshot.price}
              </span>
            </span>
          </label>
        </li>
      ))}
    </ul>
  )
}

function EmptyState({
  icon,
  title,
  body,
  action,
}: {
  icon: 'bookmark' | 'folder'
  title: string
  body: string
  action?: ReactNode
}) {
  return (
    <div className="empty-state saved-empty">
      <Icon name={icon} />
      <h3>{title}</h3>
      <p>{body}</p>
      {action}
    </div>
  )
}

export function Modal({
  title,
  subtitle,
  children,
  footer,
  onClose,
}: {
  title: string
  subtitle?: string
  children: ReactNode
  footer: ReactNode
  onClose: () => void
}) {
  const { t } = useI18n()
  const cardRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.body.style.overflow = previousOverflow
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [onClose])

  return createPortal(
    <div
      className="modal-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <div
        ref={cardRef}
        className="modal-card"
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <header className="modal-head">
          <div>
            <h2>{title}</h2>
            {subtitle ? <p>{subtitle}</p> : null}
          </div>
          <button
            className="modal-close"
            type="button"
            onClick={onClose}
            aria-label={t('saved.cancel')}
          >
            <Icon name="close" />
          </button>
        </header>
        <div className="modal-body">{children}</div>
        <footer className="modal-actions">{footer}</footer>
      </div>
    </div>,
    document.body,
  )
}
