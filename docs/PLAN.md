# Plan - extension Playnite AutoGameSoftwareSync (AGSS)

## Principe
AGSS fait toute la synchronisation. L'extension Playnite **ne lance rien, ne restaure rien, ne sauvegarde rien** : elle lit `status.json` dans le dossier de donnees AGSS (`%LOCALAPPDATA%\AutoGameSoftwareSync`, variable `AUTOGAMESOFTWARESYNC_DATA_DIR` ou chemin regle) et affiche l'etat de chaque jeu (pastille inseree dans les themes reconnus, genre de repli sinon). Ce qu'elle ecrit : le genre de repli, et depuis 1.2.0 la liste des jeux installes `playnite-library.json` a cote d'un `status.json` existant (voir `docs/PLAYNITE-LIBRARY.md`).

## Version 1.2.0 (2026-10-29) — liste des jeux installes pour AGSS
- `playnite-library.json` (format 1, `Logic/Library.cs`) ecrit a cote d'un `status.json` EXISTANT seulement (jamais dans un dossier qu'AGSS n'utilise pas ; une instance portable de test doit regler le chemin de `status.json`), fichier temporaire a nom unique puis remplacement, rien si contenu identique (date de generation ignoree), au demarrage, sur evenement de bibliotheque (anti-rebond 5 s), quand `status.json` apparait, quand le dossier ou les reglages changent, a la fermeture si une ecriture etait en attente. Reglage « Write the installed games list » (defaut actif) ; decoche ou extension desactivee = fichier supprime. Jeux emules : `roms` + `emulator` (dossier de l'emulateur resolu via l'API Playnite, `{EmulatorDir}` jamais vide). Journal : une ligne par ecriture reelle, un avertissement par cause d'echec.
- Repli genre : seuls les elements a la forme exacte `prefixe + etat[ · appareil]` sont reconnus comme « a nous » (`OwnItems`), prefixe d'au moins 4 caracteres avec un symbole : un genre de l'utilisateur n'est jamais retire.
- Tests xunit : 146.

## Version 1.1 (2026-10-27) — recettes pour les themes plein ecran populaires
- Recettes ajoutees (verifiees en copie portable) : ReskinPS5 1.11, (u)biquity 2.0.4, PS5ish 5.0.3, PlayniteModernUI 1.6.2, Playnite Deck 3.0.2, Nintendo Switch-ish 2.0.0, Toggle 1.10.1. XBOXSERIESish 2.0.0 reste en repli genre (carte de hauteur fixe, colonnes separees). Liste et dispositions : `docs/THEMES.md`.
- Nouvelles dispositions (`UI/MimicBuilder.cs`) : copie de l'entree voisine (libelle -> "AGSS", valeur -> etat colore), recherche de l'ancre par chemin de liaison uniquement dans la vue `GameDetails`.

## Version 1.0 (2026-10-27) — pastille + repli genre (remplace l'affichage ci-dessous)
- Zero reglage : dossier de donnees detecte (chemin regle > variable `AUTOGAMESOFTWARESYNC_DATA_DIR` processus/utilisateur > `%LOCALAPPDATA%\AutoGameSoftwareSync` > anciens dossiers via `Logic/Legacy.cs`), theme actif via `ApplicationSettings.FullscreenTheme/DesktopTheme`, langue de Playnite.
- Schema 1 et 2 (et suivants, champs additifs) lus ; etat `no_shared_folder` ; appareil = `origin_device`/`origin_device_color`, repli `remote_head_machine`/`machine_color` du jeu, sinon rien.
- Cascade : 1) pastille inseree (`UI/BadgeInjector.cs`, recettes `ThemeRecipes` dans `Logic/Display.cs`, Solaris d'abord) ; 2) genre `● AGSS : etat · appareil` si pas de recette ou echec durable (fiche visible 8 s sans les elements) ; 3) elements personnalises `AGSS_Badge` / `AGSS_Text` (`docs/THEMES.md`).
- Decision stable, identique en Bureau et en Plein ecran (`StableDecision`) : elle depend du theme PLEIN ECRAN configure (lu dans `fullscreenConfig.json`, sinon `ApplicationSettings.FullscreenTheme`) et de sa version (`theme.yaml`). Recette -> pas de genre ; pas de recette -> genre. La pastille n'est tentee qu'en Plein ecran. Un echec durable est memorise par version du theme (`BadgeFailedThemes`, pas de nouvel essai) ; une nouvelle version du theme retente la pastille et retire les genres. Un element personnalise vu dans un theme plein ecran est memorise (`CustomElementThemes`). Un changement de theme est pris en compte au demarrage suivant.
- Genre : remplace a chaque changement d'etat, genres AGSS inutilises supprimes, remis apres une mise a jour des jeux (ItemUpdated / OnLibraryUpdated), retire quand la pastille fonctionne.
- Migration des reglages 0.x : champ Genre, prefixe `● AGSS : `, anciens elements (`AGSS : ` dans l'ancien champ) retires une fois.
- Essais : copie portable (Solaris 1.3.6, Playnite 10.51), captures dans `AGSS-Playnite-Faisabilite\captures-v1\`. Tests xunit : 105.

## Etude (resume, 2026-10-25)
- « Polaris » n'existe pas ; le theme est **Solaris** (https://github.com/And360red/Solaris, MIT, v1.3.8 du 2026-10-21). Source XAML non publiee dans le depot (uniquement le .pthm de la release).
- Playnite 10.63 ; extensions .NET Framework 4.6.2 ; PlayniteSDK 6.18.0 ; `extension.yaml` : Id, Name, Author, Version, Module, Type (+ Icon, Links). Docs : https://api.playnite.link/docs/tutorials/extensions/events.html , https://api.playnite.link/docs/tutorials/extensions/customUiIntegration.html
- Solaris affiche les **Genres** sur la fiche du jeu, pas les Etiquettes (information fournie par l'utilisateur, non verifiee dans le XAML) : conseiller le champ **Genre** pour Solaris. Les Etiquettes restent le defaut (filtrables dans tous les themes).
- Etat local : Desktop par defaut, themes integres seulement, extension unique IGDBMetadata_Builtin.

## Schema status.json (UTF-8 sans BOM, schema 1)
```json
{ "schema":1, "app_version":"1.0.0", "updated_at_utc":"...Z", "watching":true, "shared_folder_reachable":true,
  "games":[ {"name":"","key":"","exe":"Jeu.exe","install_dir":"","save_path":"","state":"synced","last_sync_utc":null,"detail":""} ] }
```
Etats : synced, needs_backup, cloud_newer, conflict, running, never_synced, error, unreachable, paused. Le fichier doit etre ecrit de facon atomique (ecrire un temporaire puis renommer) ; l'extension tolere quand meme fichier absent/verrouille/partiel/invalide/schema inconnu (aucune exception, nouvel essai au prochain evenement ou dans les 30 s, etats deja poses conserves).

## Comportement
- Lecture : au demarrage de Playnite, FileSystemWatcher (debounce 500 ms), filet toutes les 30 s, et OnLibraryUpdated.
- Correspondance AGSS -> jeux Playnite : 1) nom d'exe (insensible a la casse) parmi les actions du jeu ; 2) nom normalise (minuscules, alphanumerique) = `key` ou `name` ; 3) dossier d'installation. Tous les jeux correspondants sont mis a jour ; aucune correspondance = rien. Jamais d'ajout/suppression de jeux.
- Affichage : un seul element prefixe (defaut `AGSS : `) dans le champ choisi (Etiquette par defaut, Genre, Caracteristique, Categorie), libelles EN/FR/RU/ES/ZH selon la langue de Playnite (repli EN). Ecriture dans la base seulement si l'element actuel differe du voulu ; seuls les elements prefixes sont retires ; pas de doublon.
- `watching:false` : seuls les etats `unreachable`/`paused` sont appliques ; rien n'est efface.
- Notifications Playnite (OFF par defaut) lors du passage a `conflict` ou `cloud_newer` (pas au premier chargement).
- Menu principal « AGSS : remove all added items » (confirmation) : retire tous les elements prefixes du champ courant. Changer de champ ou de prefixe ne migre pas les anciens elements : lancer le nettoyage avant.

## Code
`Logic/Status.cs` (modele, parseur tolerant, `IStatusSource`, `FileStatusSource`, `FakeStatusSource`), `Logic/Logic.cs` (libelles, correspondance, planificateur de mise a jour minimale, regles de notification), `AutoGameSoftwareSyncPlugin.cs` (Playnite), `Settings/`. Tests xunit (29) sur la logique pure et la lecture de fichier.

## A faire cote AGSS
Ecrire `status.json` (ecriture atomique, UTF-8 sans BOM) dans son dossier de donnees a chaque changement d'etat, avec `exe` quand connu (meilleure correspondance), `watching:false` a la fermeture.

## Tests dans Playnite (utilisateur)
1. Fermer Playnite, `dotnet build`, lancer `tools\install-local.ps1`, demarrer Playnite (Desktop). Extensions > Extensions : AutoGameSoftwareSync present, reglages visibles.
2. Placer un `status.json` de test (ou laisser AGSS l'ecrire) : l'element `AGSS : ...` apparait en ~1-2 s sur les jeux correspondants ; modifier l'etat dans le fichier : l'element change, sans doublon ; vider le fichier / le corrompre : rien ne change.
3. Verifier le journal `%APPDATA%\Playnite\playnite.log` (avertissements « AGSS »).
4. Plein ecran : relancer en mode Plein ecran avec Solaris, regler « Champ utilise » = Genres, verifier la fiche du jeu ; tester le menu de nettoyage (Desktop) puis desinstallation (`tools\uninstall-local.ps1`).

## Packaging .pext (documente seulement)
`dotnet build -c Release`, copier `AutoGameSoftwareSync.PlayniteExtension.dll` + `extension.yaml` dans un dossier, puis `Toolbox.exe pack <dossier> <sortie>` (Toolbox fourni avec Playnite). Publication : PR sur PlayniteAddonDatabase.

## Limites
Non teste dans Playnite reel ; affichage Solaris (Genres) et comportement plein ecran non verifies ; mise a jour de la base depuis le thread UI via `UIDispatcher` (suppose correct) ; modification d'un champ visible par synchronisation cloud/export Playnite eventuelle.
