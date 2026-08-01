Feature: Single-game admin/observer vertical
  The whole vertical over real HTTP + SignalR: one admin creates and owns the single game,
  observers watch its lifecycle live, and stopping frees the slot for a new first-starter.

  Scenario: Create, own, control, and hand off the single game
    Given an observer is connected
    When the admin creates a game
    Then the create response carries an admin secret
    And the observer is told the status is "Created"
    When the admin starts the game
    Then the control response status is "Running"
    And the observer is told the status is "Running"
    When the admin pauses the game
    Then the observer is told the status is "Paused"
    When the admin stops the game
    Then the observer is told the status is "NoGame"
    And creating a new game issues a different admin secret

  Scenario: A second create while a game exists is refused
    When the admin creates a game
    And another client tries to create a game
    Then the second create attempt is refused as a conflict
