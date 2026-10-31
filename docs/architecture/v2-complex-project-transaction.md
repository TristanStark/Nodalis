# Nodalis 2.0 — Migration guidée des projets Complexes (#194)

## Périmètre actuel

Ce premier moteur exécutable est limité aux projets sans référence Markdown à
réécrire. Un projet existant n'est jamais migré à l'ouverture, ni par la
simple consultation d'un inventaire. Les projets Simple, Moyen et Ultra-simple
ne sont pas migrables avec ce service.

## Étapes dans Nodalis

1. Enregistrer et fermer tous les onglets du projet et de ses sous-projets.
2. Sélectionner le projet Complexe dans le worktree.
3. Ouvrir « Migrer un projet Complexe vers la structure 2.0 » dans la palette.
4. Examiner les mouvements, les obstacles et l'empreinte SHA-256.
5. Si le plan est éligible, saisir précisément MIGRER, puis confirmer.
6. Conserver le chemin de sauvegarde et du journal affichés après publication.

La commande « Restaurer un projet Complexe avant migration » demande une
confirmation distincte. Elle ne rétablit l'ancien projet que si les deux
versions correspondent encore aux empreintes du journal.

## Préflight sans écriture

Le service contrôle le manifeste Complexe, son statut non archivé, les GUID
stables, collisions des chemins, sections personnalisées, sources absentes,
jonctions / liens symboliques et dossiers parents.

Il calcule une empreinte SHA-256 regroupant tous les noms, les dossiers
vides et les hashes du contenu de tous les fichiers du projet.

Tant qu'un réécrivain de liens fiable n'est pas intégré, toute référence
Markdown ou HTML à requalifier dans les notes actives peut bloquer la
migration. Le contrôle est volontairement conservateur : même les liens
externes Markdown peuvent empêcher ce premier lot de s'exécuter. Les
templates et journaux techniques sont exclus du balayage des liens actifs.

## Sauvegarde, journal et transaction

Une transaction approuvée conserve les données dans :

    Imports/.complex-v2-migrations/<uuid>/
      journal.json
      original/
      staging/                  si publication non achevée
      previously-published/     après restauration

Le projet est copié en staging. Les sections physiques sont déplacées
dans cette copie seulement, les identifiants de sections existantes sont
conservés, et les nouveaux conteneurs obtiennent de nouveaux GUID.

Après une nouvelle vérification de l'empreinte originale, le journal est
écrit AVANT les deux renommages de répertoires sur le même volume :

1. Original vers original/ dans le dossier de transaction.
2. Revérification de l'original conservé.
3. Staging vers l'emplacement initial du projet.
4. Vérification de l'empreinte publiée et journal terminé.

L'original est conservé sans purge automatique. En cas de crash, Nodalis
demande explicitement une récupération au démarrage avant d'ouvrir le
workspace en écriture. Un projet concurrent ou modifié n'est jamais
écrasé automatiquement. La restauration d'un projet migré garde aussi
la version publiée dans previously-published/.

## Limites et qualification

- Les références Markdown, liens relatifs et images nécessitant des
  réécritures bloquent l'exécution ; pas de déplacement partiel.
- Une copie intégrale du projet nécessite un espace disque suffisant.
- Un autre logiciel peut encore modifier les fichiers en parallèle ;
  les vérifications d'empreintes et le verrou du moteur limitent ce risque,
  sans offrir un verrou global sur les logiciels externes.
- Sauvegardes et journaux restent disponibles jusqu'à suppression explicite.
- La qualification visuelle WPF sous Windows reste nécessaire avant release.

Tests automatiques : approbation obligatoire, plan périmé, protection des
liens, GUID stables, sauvegarde exacte, interruption avant/après publication,
récupération, refus de rollback après édition et restauration interrompue.
