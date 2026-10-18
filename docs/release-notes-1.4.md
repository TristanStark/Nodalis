# Nodalis v1.4.0 — Analyse d'impact et exploration des dépendances

**Version stable du 18 octobre 2026 · Windows 10/11 x64 · .NET 10 (portable autonome)**

Cette version réunit les deux étapes de l’EPIC [#98](https://github.com/TristanStark/Nodalis/issues/98) :
le moteur d'analyse d'impact de v1.3 et son explorateur visuel et documentaire de v1.4.
Elle s'appuie sur les relations Markdown **déjà présentes dans Nodalis**,
sans nouvelle base métier, compte, IA, accès réseau ou dépendance tierce.

## Fonctionnalités majeures

### 1. Répondre à « Si ce document change, qu'est-ce qui pourrait être affecté ? »

Depuis un document dans l'explorateur, utiliser **Relations → Impacts…**
ou la **palette de commandes (Ctrl+P)**. La vue liste présente :

- les documents potentiellement concernés, du voisin immédiat jusqu'aux dépendances indirectes ;
- le **sens exact** des relations, leur profondeur et la chaîne de preuves ;
- la ligne Markdown où chaque relation est déclarée, avec navigation vers la source ;
- les filtres de texte, portée Projet/Application/Module/Global, profondeur et preuve.

Les relations `dépend de`, `implémente`, `teste` et `documente` peuvent
propager un impact du document **cible** vers celui qui porte la relation.
Les liens Markdown simples, `bloque`, `remplace`, `est lié à` et les types
inconnus restent des **suggestions de contexte**, sans propagation causale.

**Aucun résultat ne signifie qu'une panne est certaine.** La relation
explique pourquoi un document pourrait mériter une vérification.

### 2. Explorer un graphe WPF natif

Un onglet **Graphe visuel** complète la vue liste accessible au clavier :

- flèches orientées pour distinguer le document qui change de ses dépendants ;
- traits continus pour les relations déclarées, pointillés pour le contexte ;
- zoom de 40 à 180 %, recentrage, défilement et sélection synchronisée ;
- regroupement par profondeur, projet ou type de document ;
- mise en évidence du chemin justificatif sélectionné.

Le dessin est limité à **90 nœuds** pour préserver la réactivité WPF.
La liste reste disponible et indépendante de cette limite d'affichage.

### 3. Relier les incidents et interventions de maintenance

L'**Historique de maintenance** propose **Analyser les impacts** depuis
un incident ou une intervention contenant une relation explicite vers un document.
L'onglet **Maintenance liée** présente les références des fiches de maintenance
aux documents visibles dans l'analyse, avec résumé, résolution, identifiant
de bloc et emplacement Markdown. Un clic permet d'ouvrir le bloc source.

Les ressemblances de texte n'engendrent aucune dépendance inventée.
Une référence cassée ne sera pas redirigée vers un document homonyme.

### 4. Accélération et robustesse

- Graphe local **dérivé et reconstruisible** depuis les identifiants GUID
  et relations existants ; aucun cache opaque obligatoire.
- Réutilisation d'un instantané en mémoire lorsque l'index n'a pas changé.
- Réindexation des **sources de relations modifiées** et des sources touchées
  par une suppression/restauration ; comparaison en tests avec une reconstruction totale.
- Parcours borné (jusqu'à 250 résultats, 1 500 nœuds explorés, profondeur paramétrable),
  cycles évités, troncature annoncée, annulation coopérative.
- Analyse en **lecture seule** : aucun document ni registre d'identifiants
  n'est modifié par un simple calcul.

## Mise à jour depuis les versions 1.0 à 1.2

1. Effectuer une **sauvegarde du workspace** dans la version actuelle.
2. Télécharger `Nodalis-win-x64.zip` et vérifier la somme SHA-256 fournie
   dans `SHA256SUMS.txt`.
3. Fermer Nodalis, extraire la nouvelle archive dans un dossier d'application
   et lancer `Nodalis.exe`.
4. Ouvrir son workspace existant : les sources Markdown/JSON et les identifiants
   stables sont conservés. Aucune migration métier obligatoire n'est créée
   pour l'analyse d'impact.

Les fonctionnalités **Inbox 1.1**, **maintenance 1.2**, tâches, calendrier,
recherche exacte/fuzzy et notes rapides continuent de fonctionner sans IA.

## Documentation et limites connues

- [Guide pas à pas](https://github.com/TristanStark/Nodalis/blob/v1.4.0/docs/impact-user-guide-1.4.md)
- [Règles de propagation](https://github.com/TristanStark/Nodalis/blob/v1.4.0/docs/impact-semantics-1.3.md)
- [Checklist de qualification](https://github.com/TristanStark/Nodalis/blob/v1.4.0/docs/release-checklist-1.4.md)
- [Historique des changements et issues](https://github.com/TristanStark/Nodalis/issues/98)

Les suggestions ne sont **jamais** des dépendances confirmées ;
le périmètre interprojets doit être activé volontairement ;
le graphe est une vue **bornée**, non un schéma exhaustif au-delà
des limites affichées. Après des changements réalisés hors de Nodalis,
actualiser les liens depuis Nodalis avant de relancer l'analyse.

## Qualification

Les issues [#113–#121](https://github.com/TristanStark/Nodalis/issues/98)
et l'EPIC #98 sont clôturées après validation utilisateur.
La CI Windows couvre compilation WPF, style explicite, absence de packages tiers,
régression, cycles/renommages/références invalides, cache incrémental,
construction portable reproductible et démarrage de l'exécutable publié.

**Téléchargement :** utiliser exclusivement les fichiers joints à cette
release : `Nodalis-win-x64.zip` et `SHA256SUMS.txt`.
