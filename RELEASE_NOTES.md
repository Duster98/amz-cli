# AMZ CLI 3.2.8

This release includes all 3.2.7 features.

## Correct Amazon offer links

- Amazon-sold buying options now resolve the marketplace's Amazon retail seller
  ID, so "View offer" opens the intended Amazon offer instead of the current
  buy box.
- The product cache schema changed, so entries written by older builds are
  fetched again instead of reusing the incorrect links.
