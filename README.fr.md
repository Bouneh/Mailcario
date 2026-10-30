<p align="center">
  <img src="docs/assets/mailcario-wordmark.svg" alt="Mailcario" width="430">
</p>

<h3 align="center">Une boîte mail familière, à votre façon.</h3>

<p align="center">
  Un client de messagerie Windows gratuit et open source.<br>
  Plusieurs comptes, une interface inspirée d’Outlook, des invitations d’agenda et la confidentialité par défaut.
</p>

<p align="center">
  <a href="https://github.com/Bouneh/Mailcario/releases/latest"><img alt="Dernière version" src="https://img.shields.io/github/v/release/Bouneh/Mailcario?style=flat-square&color=11747B"></a>
  <a href="LICENSE"><img alt="Licence MIT" src="https://img.shields.io/badge/licence-MIT-11747B?style=flat-square"></a>
  <img alt="Windows 10 et 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-11747B?style=flat-square">
  <a href="https://github.com/Bouneh/Mailcario/stargazers"><img alt="Étoiles GitHub" src="https://img.shields.io/github/stars/Bouneh/Mailcario?style=flat-square&color=11747B"></a>
</p>

<p align="center">
  <a href="https://github.com/Bouneh/Mailcario/releases/latest"><strong>Télécharger pour Windows</strong></a>
  &nbsp;·&nbsp; <a href="README.md">Read in English</a>
  &nbsp;·&nbsp; <a href="CONTRIBUTING.md">Contribuer</a>
</p>

<p align="center"><img src="docs/images/inbox-dark.png" alt="Boîte de réception Mailcario avec dossiers, liste de messages et volet de lecture" width="100%"></p>
<p align="center"><sub>Capture de démonstration avec des comptes et des messages fictifs.</sub></p>

## Votre messagerie sur Windows

| Une interface claire | Les fonctions du quotidien | La confidentialité intégrée |
| --- | --- | --- |
| Affichage en trois volets inspiré d’Outlook, colonnes redimensionnables, dossiers, recherche dans le dossier et thèmes clair et sombre. | Plusieurs comptes IMAP, envoi SMTP, réponses, transfert, pièces jointes, signatures de tous les comptes, carnet d’adresses local, règles et notifications. | Images distantes bloquées par défaut, pixels de suivi détectés et bloqués, scripts HTML désactivés. |

Mailcario est un **client de messagerie local pour Windows**. Aucun serveur personnel n’est nécessaire : il utilise votre fournisseur de messagerie actuel. Les paramètres restent sur votre PC ; les identifiants enregistrés sont protégés pour votre compte Windows.

Chaque dossier charge d’abord les messages récents, puis les plus anciens quand vous faites défiler la liste ou cliquez sur **Charger les messages précédents**. Les résultats de recherche continuent aussi au-delà de la première page.

### Tous les comptes dans une seule vue

Choisissez **Toutes les boîtes** dans les Favoris pour lire ensemble les messages de vos comptes connectés. La recherche de cette vue interroge tous les dossiers sélectionnables de chaque compte, ainsi que les copies enregistrées dans les dossiers locaux. Chaque résultat indique son compte et son dossier ; les résultats serveur plus anciens se chargent pendant le défilement. Activez **Conversations** pour réunir les messages reçus et les réponses envoyées, tout en gardant l’état lu ou non lu de chaque message.

![Boîte unifiée et conversations avec des comptes fictifs](docs/images/unified-dark.png)

Un clic droit sur un message permet de lui attribuer une catégorie colorée, de le reporter d’une heure, jusqu’au lendemain ou d’une semaine, ou d’utiliser une action rapide comme **Marquer comme lu et archiver**. Le message reporté réapparaît à l’heure prévue pendant que Mailcario fonctionne. Raccourcis : `Ctrl+N` nouveau message, `Ctrl+E` rechercher, `Ctrl+R` répondre, `Ctrl+Maj+R` répondre à tous, `Ctrl+F` transférer, `Ctrl+Q` marquer comme lu, `Ctrl+U` marquer comme non lu et `Suppr` déplacer le message sélectionné vers la corbeille.

Le ruban de rédaction propose **Modèles** pour enregistrer et réutiliser un objet et un corps mis en forme. Après un clic sur **Envoyer**, la fenêtre de rédaction se ferme et un bouton **Annuler** apparaît en bas à gauche de la fenêtre principale pendant 10 secondes par défaut. L’annulation interrompt l’envoi en attente et rouvre le brouillon. Le délai se règle dans **Paramètres** : 0, 5, 10, 20 ou 30 secondes. Dès que l’envoi SMTP commence, le message ne peut plus être rappelé. **Options → Envoi différé** conserve le brouillon et une copie privée de ses pièces jointes sur ce PC. Mailcario l’envoie à l’heure choisie ou dès qu’il fonctionne de nouveau. Si l’application ou le PC est arrêté, l’envoi attend le prochain démarrage. **Fichier → Envois programmés** permet de consulter ou d’annuler les messages en attente et de relancer manuellement un envoi interrompu ou échoué. Les brouillons programmés, modèles et catégories sont enregistrés sous `%LOCALAPPDATA%\Mailcario` et ne sont pas synchronisés avec d’autres appareils.

Mailcario ajoute au carnet d’adresses local les correspondants des messages qu’il charge et des envois réussis. Il parcourt aussi progressivement les en-têtes des anciens mails, dossier par dossier, sans télécharger les corps ni les pièces jointes. Dans les champs **À**, **Cc** et **Cci**, saisissez un nom ou une adresse pour voir les suggestions ; choisissez avec les flèches et Entrée, ou cliquez sur une suggestion.

Un clic droit sur un dossier permet d’activer ou de désactiver ses notifications. Un message déplacé par une règle Mailcario peut ainsi déclencher une alerte depuis son dossier de destination ; un clic sur l’alerte ouvre ce dossier. Les options générales de notification, de son et d’aperçu restent dans **Paramètres**.

### Dossiers locaux

Utilisez **+** à côté de **Dossiers locaux** pour créer un dossier sur ce PC. Sélectionnez un ou plusieurs messages du serveur, puis glissez-les sur un dossier local ou faites un clic droit et choisissez **Copier dans un dossier local**. Mailcario conserve le message complet, pièces jointes comprises, pour le lire hors connexion. Les originaux restent dans la boîte mail jusqu’à ce que vous les supprimiez séparément. Un clic droit permet de renommer ou de supprimer un dossier local. Les copies sont enregistrées dans `%LOCALAPPDATA%\Mailcario\LocalFolders` et ne sont pas synchronisées avec d’autres appareils ; pensez à inclure ce répertoire dans vos sauvegardes.

Les pièces jointes reçues apparaissent dans le volet de lecture. Vous pouvez les télécharger, prévisualiser les PDF, images et fichiers texte dans Mailcario, et imprimer ces formats. Les autres types de fichiers peuvent être enregistrés puis ouverts avec une application compatible.

### Aperçu

| Rédaction | Import de comptes Thunderbird |
| --- | --- |
| ![Fenêtre de rédaction Mailcario](docs/images/compose-dark.png) | ![Import de comptes IMAP Thunderbird](docs/images/thunderbird-import.png) |

Le ruban de rédaction propose le presse-papiers, la mise en forme, les palettes de couleurs, les listes numérotées et la vérification des destinataires. Vous pouvez glisser des fichiers dans un brouillon et changer de signature enregistrée. Le nombre de mails non lus apparaît sur l’icône de la barre des tâches et se met à jour quand un message est ouvert.

<details>
<summary><strong>Protection lors de la lecture</strong></summary>

![Bandeaux de protection des images et pixels de suivi](docs/images/privacy-dark.png)

Les images distantes sont bloquées par défaut. Un bandeau permet de charger les images ordinaires si vous le souhaitez ; les pixels de suivi détectés restent bloqués. Les images intégrées aux signatures peuvent aussi être affichées sur demande, sans contacter d’hébergeur externe. Dans les **Paramètres**, vous pouvez activer le téléchargement automatique des images séparément pour chaque compte. Leur hébergeur peut alors connaître votre adresse IP et l’ouverture du message ; la détection du suivi n’est pas infaillible.
</details>

## Commencer

1. Téléchargez `Mailcario-Setup-X.Y.Z.exe` depuis la [dernière version](https://github.com/Bouneh/Mailcario/releases/latest).
2. Lancez l’installateur. Mailcario s’installe pour votre compte Windows sans droits administrateur.
3. Ajoutez une adresse, ou utilisez **Fichier → Importer depuis Thunderbird** pour reprendre les comptes IMAP et leurs paramètres de serveur.
4. Connectez-vous auprès de votre fournisseur. Les mots de passe et les messages locaux de Thunderbird ne sont pas copiés.

Au démarrage, Mailcario cherche une version plus récente publiée sur GitHub. Vous pouvez choisir **Plus tard** ou **Télécharger la mise à jour**. Le téléchargement est vérifié avec son empreinte SHA-256, puis Mailcario s’installe et se rouvre automatiquement. Les nouvelles versions sont construites et publiées par GitHub Actions quand la version du projet augmente sur `main`.

L’onglet **À propos** affiche la version installée et permet d’ouvrir le dépôt, de rechercher ou de télécharger une mise à jour à tout moment. Faites glisser la séparation sous la liste des comptes pour en régler la hauteur.

Un nouveau mail apparaît dans une notification Windows avec un court extrait du message. Fermer la fenêtre laisse Mailcario actif dans la zone de notification ; son icône permet de rouvrir la boîte ou de quitter l’application.

### Fournisseurs

| Fournisseur | Connexion |
| --- | --- |
| La Poste | IMAP/SMTP avec les identifiants du compte |
| Gmail | Connexion Google dans le navigateur si l’application OAuth autorise votre compte ; sinon, mot de passe d’application. L’accès OAuth public dépend de la validation par Google. |
| iCloud | IMAP/SMTP avec un mot de passe spécifique à l’application |
| Autres fournisseurs | Paramètres IMAP/SMTP manuels |
| Outlook / Hotmail | La prise en charge OAuth existe, mais un identifiant d’application Microsoft doit être configuré pour se connecter |

Mailcario sait lire les invitations d’agenda reçues par e-mail et y répondre. L’agenda affiché est pour l’instant local ; la synchronisation complète avec Google ou Microsoft n’appartient pas à cette première version.

## Développé ouvertement

Mailcario est écrit en **C# / WPF avec .NET 10**. Le dépôt contient le client, l’installateur Inno Setup, l’assistant de mise à jour et un outil de prévisualisation qui génère des captures avec des données fictives.

```powershell
dotnet build Mailcario.csproj -c Release
dotnet run --project Preview/MailcarioPreview.csproj -c Release -- --thunderbird-import-check
```

Consultez [CONTRIBUTING.md](CONTRIBUTING.md) pour contribuer et [Installer/INSTALL_AND_UPDATES.md](Installer/INSTALL_AND_UPDATES.md) pour préparer une version.

## Historique des étoiles

[![Historique des étoiles Mailcario](docs/assets/star-history.svg)](https://star-history.com/#Bouneh/Mailcario&Date)

Le graphique est actualisé par GitHub Actions. [Explorer sur Star History](https://star-history.com/#Bouneh/Mailcario&Date).

## Licence

Mailcario est distribué sous [licence MIT](LICENSE). Les contributions sont les bienvenues.
