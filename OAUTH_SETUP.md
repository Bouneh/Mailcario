# Connexion OAuth de Mailcario

## Google / Gmail

Mailcario ouvre la page officielle Google dans le navigateur du PC. Le retour vers l’application passe par un port local temporaire (`127.0.0.1`) avec PKCE et un `state` aléatoire. Il n’y a pas de serveur Mailcario à héberger. Le jeton de renouvellement est conservé dans le profil Windows, chiffré avec DPAPI.

L’identifiant OAuth doit être créé comme **application de bureau** dans Google Auth Platform et placé dans `oauth-providers.json`, champ `googleClientId`. Pour les essais locaux, placer le code secret du même client dans `oauth-providers.local.json` avec les champs `googleClientId` et `googleClientSecret`. Ce fichier est exclu de Git et copié dans le dossier de sortie lors de la compilation. Le point d’échange Google demande actuellement ce paramètre pour certains clients de bureau, même avec PKCE, alors que sa documentation le marque facultatif. Le code secret d’une application de bureau ne constitue pas une protection : Google considère ce type de client comme public. PKCE et `state` restent indispensables.

Pour IMAP et SMTP, Google demande le scope `https://mail.google.com/`. Si l’application OAuth est en mode **Testing**, ajouter chaque adresse Gmail dans la liste des utilisateurs de test. Dans ce mode, Google limite le nombre d’utilisateurs et les autorisations expirent au bout de sept jours ; il faut alors reconnecter le compte. Pour une distribution publique sans cet écran de test, l’application doit passer la vérification OAuth de Google pour ce scope restreint. Cette vérification est indépendante de l’hébergement de Mailcario.

Pour les essais personnels : dans Google Cloud Console, choisir le projet qui contient l’identifiant OAuth de Mailcario, puis **Google Auth Platform → Audience → Utilisateurs tests → Ajouter des utilisateurs**. La rubrique **Branding** peut contenir le nom Mailcario, une adresse d’assistance et le logo `Assets/Mailcario-512.png`. Les liens de site, de confidentialité et de conditions ainsi que les domaines autorisés peuvent rester vides pendant les tests. L’ajout d’un logo ne débloque pas une erreur `403 access_denied` : c’est la liste des utilisateurs tests qui la résout.

Pour une diffusion publique, Google demande une page d’accueil publique décrivant l’application, une politique de confidentialité cohérente avec le traitement réel des données et un domaine dont la propriété est vérifiée dans Search Console. Un dépôt sur `github.com` n’est pas, à lui seul, un domaine détenu par l’éditeur. GitHub Pages peut héberger gratuitement ces pages, sous réserve que l’adresse retenue soit acceptée et vérifiable par Google. Déclarer `https://mail.google.com/` dans **Data Access**, puis suivre le parcours de vérification du branding et du scope restreint. Ne publier aucune URL fictive. Pour que les utilisateurs finaux se connectent sans configuration technique, il faudra fournir les paramètres du client de bureau avec l’application distribuée, ou disposer d’un autre mécanisme d’échange. Aucune donnée d’utilisateur ou jeton ne doit être publié.

Références : [OAuth pour applications de bureau](https://developers.google.com/identity/protocols/oauth2/native-app), [XOAUTH2 Gmail](https://developers.google.com/workspace/gmail/imap/xoauth2-protocol), [état Testing](https://support.google.com/cloud/answer/15549945?hl=fr), [exigences de la page d’accueil](https://support.google.com/cloud/answer/13807376?hl=en), [vérification des scopes restreints](https://developers.google.com/identity/protocols/oauth2/production-readiness/restricted-scope-verification).

## Microsoft / Outlook.com

Outlook.com et Hotmail exigent OAuth2 pour IMAP et SMTP. Mailcario utilise le flux « device code » : le navigateur sert à autoriser le compte, puis les jetons restent sur le PC, chiffrés avec Windows DPAPI. Aucun serveur Mailcario n’est nécessaire.

L’application a toutefois besoin d’un **identifiant public d’application Microsoft**. C’est une inscription unique du projet, à faire par la personne qui distribue Mailcario. Les utilisateurs finaux n’auront pas à créer leur propre inscription une fois l’identifiant fourni avec l’application.

Tant que cet identifiant n’est pas fourni avec Mailcario, le bouton de connexion Outlook/Hotmail reste indisponible. Le client ne demande pas aux utilisateurs de créer une application ou de saisir un identifiant de locataire.

1. Dans Microsoft Entra, enregistrer une application acceptant les **comptes Microsoft personnels** et autoriser le **flux de client public**.
2. Ajouter les autorisations déléguées Exchange Online `IMAP.AccessAsUser.All` et `SMTP.Send`, ainsi que `offline_access`.
3. Copier l’**Application (client) ID** dans `oauth-providers.json`, champ `microsoftClientId`, puis reconstruire Mailcario. Ne pas créer ni intégrer de secret client dans l’application.
4. Dans Outlook.com, activer l’accès IMAP si le compte ne l’a pas déjà autorisé.

Le dépôt et l’application n’ont pas besoin de l’identifiant du répertoire Entra : la connexion des comptes personnels utilise le point d’entrée Microsoft `consumers`. L’identifiant d’application est public par conception ; aucun jeton ou mot de passe utilisateur ne doit être publié.

La première connexion demande une validation sur le site officiel Microsoft. Mailcario stocke ensuite le jeton de renouvellement dans le profil Windows de l’utilisateur.

Références : [authentification OAuth pour IMAP/SMTP](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth), [paramètres Outlook.com](https://support.microsoft.com/fr-fr/outlook/pop-imap-and-smtp-settings-for-outlook-com), [flux device code](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code).
