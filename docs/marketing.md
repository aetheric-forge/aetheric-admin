# Marketing campaign integration

The Marketing institution owns drafts and publication operations. This host integrates by convention:
public types/UI remain host-owned, and no shared generic catalog/repository contract is introduced.

Initial targets: Black Circuit `/portfolio` and Aetheric Forge `/projects`.
Admin routes: `/marketing/campaigns` and `/marketing/campaigns/{id}`. Legacy `/campaigns` tracking stays intact.
Live content is an explicit snapshot. Saving drafts does not change the website; ending restores baseline.
Public content refreshes within 30 seconds on page requests; reload an already-open page.

Configure the corresponding admin and public reader to use the same Marketing Mongo database. To manage
both targets from either admin, all hosts must share that database. Public Mongo users should have read
access to `marketingPublications` (and `offerings`) and no read/write access to private `marketingCampaigns`.
Admin Mongo users need authoring/publication write access and permission to create the unique target index.

Read the institution's [publication guide](https://github.com/aetheric-forge/marketing-campus/blob/feat/campaign-publication/docs/publication.md)
for schema, authorization, concurrency, legacy migration boundaries, and end-to-end acceptance.

Marketing is opt-in through `Marketing:MongoDb`. Endpoint defaults may come from platform Mongo settings, but username/password must be Marketing-owned; existing institution credential policy applies. An absent Marketing section leaves the public reader empty and admin management unavailable.
