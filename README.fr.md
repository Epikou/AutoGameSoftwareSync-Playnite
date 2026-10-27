# AutoGameSoftwareSync pour Playnite

[English](README.md)

<p align="center"><img src="assets/icon.png" width="96" alt="AutoGameSoftwareSync"></p>

Extension [Playnite](https://playnite.link) gratuite en **lecture seule** : elle affiche, pour chaque jeu, ce que [AutoGameSoftwareSync](https://github.com/Epikou/AutoGameSoftwareSync) (AGSS) sait de ses sauvegardes :

- l'état : **Protégé**, **À envoyer**, **Conflit**, **Cloud plus récent**...
- la date de la dernière synchronisation ;
- l'appareil d'où vient la version en place, dans sa couleur.

AGSS fait tout le travail. L'extension lit seulement le fichier `status.json` qu'AGSS écrit. Elle ne lance, ne restaure et ne sauvegarde rien, et n'écrit jamais dans tes sauvegardes ni dans ton coffre.

![Pastille dans Solaris](screenshots/solaris-badge.png)
![Conflit dans Solaris](screenshots/solaris-conflict.png)

## Thèmes plein écran reconnus

Pour ces thèmes, une pastille est insérée dans la fiche du jeu, dans le style du thème. Rien à régler.

| Thème | Version vérifiée |
|---|---|
| Solaris | 1.3.6 |
| ReskinPS5 | 1.11 |
| (u)biquity | 2.0.4 |
| PS5ish | 5.0.3 |
| PlayniteModernUI | 1.6.2 |
| Playnite Deck | 3.0.2 |
| Nintendo Switch-ish | 2.0.0 |
| Toggle | 1.10.1 |

Captures : voir le [README anglais](README.md#supported-fullscreen-themes).

### Les autres thèmes : le genre de repli

Si le thème plein écran actif n'est pas dans la liste (par exemple XBOXSERIESish, dont la carte de détails à taille fixe ne peut pas recevoir une ligne de plus), l'extension ajoute un genre du type `● AGSS : Protégé · MONPC`. Le `●` le place en tête de liste. Il est remplacé quand l'état change, retiré quand il ne sert plus et remis après un téléchargement de métadonnées. Aucun genre n'est écrit tant qu'une pastille fonctionne.

![Genre de repli](screenshots/genre-fallback.png)

Les auteurs de thèmes peuvent placer l'information eux-mêmes : voir [docs/THEMES.md](docs/THEMES.md) (en anglais).

## Prérequis

- Playnite 10 (API d'extensions 6.15 ou plus récente).
- AutoGameSoftwareSync **2.0 ou plus récent**. Le meilleur affichage demande la **2.0.2 ou plus récente** (la version qui fournit le nom et la couleur de l'appareil dans `status.json`) ; avec la 2.0.0, l'état et la date s'affichent mais pas l'appareil.

## Installation

1. Télécharge `AutoGameSoftwareSync_Playnite_Plugin_1_1_0.pext` depuis la [dernière version](https://github.com/Epikou/AutoGameSoftwareSync-Playnite/releases/latest).
2. Double-clique dessus (ou glisse-le dans Playnite) et confirme l'installation. Redémarre Playnite si demandé.
3. C'est tout : le dossier de données AGSS, le thème actif et la langue sont détectés automatiquement, et l'affichage suit `status.json` en direct.

Les réglages restent disponibles dans Playnite (Extensions > Paramètres des extensions) : mode d'affichage, champ, préfixe, chemin de `status.json`, notifications facultatives sur Conflit / Cloud plus récent (désactivées par défaut).

Les textes suivent la langue de Playnite : anglais, français, russe, espagnol et chinois.

## À propos

Projet « vibecodé » : l'extension a été testée à la main dans Playnite 10 avec les thèmes ci-dessus, et accompagnée d'une suite de tests automatiques. Elle est gratuite ; si elle te sert, tu peux soutenir le projet sur [Ko-fi](https://ko-fi.com/autogamesoftwaresave).

Questions, aide et nouveautés : rejoins le [Discord](https://discord.gg/W8YXUJAK4e).

Le code source est visible dans ce dépôt (`src`, `tests`) pour que tu puisses lire et auditer ce que fait l'extension. Ce n'est pas un logiciel libre : voir [LICENSE](LICENSE). Pour compiler et tester : `dotnet build` / `dotnet test` (.NET Framework 4.6.2, PlayniteSDK 6.15.0).

## Licence

Gratuit pour un usage personnel, code visible mais tous droits réservés : voir [LICENSE](LICENSE). Playnite et PlayniteSDK appartiennent à leurs auteurs respectifs.
